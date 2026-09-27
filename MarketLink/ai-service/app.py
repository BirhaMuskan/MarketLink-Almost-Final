from __future__ import annotations

from datetime import date, datetime
from math import ceil
from typing import List

import numpy as np
from fastapi import FastAPI
from pydantic import BaseModel, Field
from sklearn.ensemble import RandomForestRegressor
from sklearn.metrics import mean_absolute_error


app = FastAPI(
    title="MarketLink Free Local AI Service",
    version="1.0.0",
    description="Local, no-paid-API demand forecasting service for MarketLink."
)


class HistoricalDemandRow(BaseModel):
    date: datetime
    unitPrice: float = Field(ge=0)
    stockQuantity: float = Field(ge=0)
    reservedQuantity: float = Field(ge=0)
    soldQuantity: float = Field(ge=0)


class ForecastRequest(BaseModel):
    farmerProductId: int
    farmerMarketId: int
    predictionDate: datetime

    currentReservations: float = Field(ge=0)
    currentStock: float = Field(ge=0)
    currentPrice: float = Field(ge=0)

    history: List[HistoricalDemandRow]


class ForecastResponse(BaseModel):
    algorithm: str
    predictedDemand: float
    recommendedStock: float
    shortageRisk: str
    confidenceScore: float
    historicalAverage: float
    mae: float
    mape: float
    trainingRows: int
    explanation: str


def week_of_year(value: datetime) -> int:
    return int(value.isocalendar().week)


def features_for_row(row: HistoricalDemandRow) -> list[float]:
    return [
        float(row.date.weekday()),
        float(row.date.month),
        float(week_of_year(row.date)),
        float(row.unitPrice),
        float(row.stockQuantity),
    ]


def feature_for_future(req: ForecastRequest) -> list[float]:
    return [
        float(req.predictionDate.weekday()),
        float(req.predictionDate.month),
        float(week_of_year(req.predictionDate)),
        float(req.currentPrice),
        float(req.currentStock),
    ]


def demand_target(row: HistoricalDemandRow) -> float:
    # Historical demand signal:
    # sold quantity is realized demand;
    # any still-reserved quantity is also known demand for that market date.
    return max(0.0, float(row.soldQuantity + row.reservedQuantity))


def weighted_average(values: list[float]) -> float:
    if not values:
        return 0.0

    weights = np.arange(1, len(values) + 1, dtype=float)
    arr = np.asarray(values, dtype=float)

    return float(np.average(arr, weights=weights))


def round_stock(value: float) -> float:
    # Round upward to 0.5 unit for practical preparation quantities.
    return ceil(max(0.0, value) * 2.0) / 2.0


def calculate_mape(actual: np.ndarray, predicted: np.ndarray) -> float:
    non_zero = np.abs(actual) > 1e-9

    if not np.any(non_zero):
        return 0.0

    return float(
        np.mean(
            np.abs(
                (actual[non_zero] - predicted[non_zero])
                / actual[non_zero]
            )
        ) * 100.0
    )


@app.get("/health")
def health():
    return {
        "status": "ok",
        "service": "MarketLink Free Local AI",
        "cost": "no paid API"
    }


@app.post("/forecast-demand", response_model=ForecastResponse)
def forecast_demand(req: ForecastRequest):
    ordered = sorted(req.history, key=lambda x: x.date)

    targets = [demand_target(x) for x in ordered]
    historical_average = (
        float(np.mean(targets))
        if targets
        else 0.0
    )

    training_rows = len(ordered)

    mae = 0.0
    mape = 0.0

    # With limited weekly history a moving average is safer than pretending
    # a fitted ML model is reliable.
    if training_rows < 8:
        base_prediction = weighted_average(targets)

        predicted = max(
            base_prediction,
            float(req.currentReservations)
        )

        confidence = min(
            0.60,
            0.30 + training_rows * 0.035
        )

        algorithm = "WeightedMovingAverage"

    else:
        X = np.asarray(
            [features_for_row(x) for x in ordered],
            dtype=float
        )

        y = np.asarray(targets, dtype=float)

        # Time-respecting validation:
        # earlier rows train, latest rows validate.
        validation_count = max(2, int(round(training_rows * 0.20)))
        split_index = training_rows - validation_count

        if split_index >= 5:
            X_train = X[:split_index]
            y_train = y[:split_index]

            X_test = X[split_index:]
            y_test = y[split_index:]

            validation_model = RandomForestRegressor(
                n_estimators=160,
                random_state=42,
                min_samples_leaf=1,
                n_jobs=-1
            )

            validation_model.fit(X_train, y_train)

            validation_prediction = validation_model.predict(X_test)

            mae = float(
                mean_absolute_error(
                    y_test,
                    validation_prediction
                )
            )

            mape = calculate_mape(
                y_test,
                validation_prediction
            )

        final_model = RandomForestRegressor(
            n_estimators=220,
            random_state=42,
            min_samples_leaf=1,
            n_jobs=-1
        )

        final_model.fit(X, y)

        future_feature = np.asarray(
            [feature_for_future(req)],
            dtype=float
        )

        model_prediction = float(
            final_model.predict(future_feature)[0]
        )

        predicted = max(
            0.0,
            model_prediction,
            float(req.currentReservations)
        )

        # Confidence is a simple evaluation indicator, not a probability.
        # Lower validation error = higher score.
        if mape <= 0:
            confidence = 0.75
        else:
            confidence = max(
                0.35,
                min(
                    0.95,
                    1.0 - (mape / 150.0)
                )
            )

        algorithm = "RandomForestRegressor"

    # Small safety buffer for preparation planning.
    recommended_stock = round_stock(
        max(
            predicted * 1.08,
            float(req.currentReservations)
        )
    )

    if req.currentStock <= 0:
        shortage_risk = "High" if predicted > 0 else "Low"
    else:
        ratio = predicted / float(req.currentStock)

        if ratio >= 1.0:
            shortage_risk = "High"
        elif ratio >= 0.75:
            shortage_risk = "Medium"
        else:
            shortage_risk = "Low"

    predicted = round(predicted, 3)
    recommended_stock = round(recommended_stock, 3)
    historical_average = round(historical_average, 3)
    mae = round(mae, 3)
    mape = round(mape, 3)
    confidence = round(confidence, 4)

    explanation = (
        f"Based on {training_rows} historical market rows, "
        f"MarketLink predicts approximately {predicted:.3f} units of demand "
        f"and recommends preparing about {recommended_stock:.3f} units. "
        f"Current reservations are {req.currentReservations:.3f}. "
        f"Shortage risk is {shortage_risk}."
    )

    return ForecastResponse(
        algorithm=algorithm,
        predictedDemand=predicted,
        recommendedStock=recommended_stock,
        shortageRisk=shortage_risk,
        confidenceScore=confidence,
        historicalAverage=historical_average,
        mae=mae,
        mape=mape,
        trainingRows=training_rows,
        explanation=explanation
    )
