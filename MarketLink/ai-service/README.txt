MARKETLINK FREE LOCAL AI SERVICE

COST
====
No OpenAI.
No Gemini.
No Azure AI.
No paid API.
No cloud database required.

Uses:
- Python
- FastAPI
- scikit-learn
- NumPy
All can run locally on the same Windows computer.

START
=====
Double-click:

start-ai.bat

First run creates a Python virtual environment and downloads the free
open-source Python packages.

When running successfully:

http://127.0.0.1:8001/health

should return status = ok.

Swagger test page:

http://127.0.0.1:8001/docs

ALGORITHM
=========
If history has fewer than 8 weekly rows:
- Weighted Moving Average fallback
- lower confidence

If history has 8+ rows:
- RandomForestRegressor
- time-respecting holdout validation
- MAE
- MAPE-derived confidence indicator

DATA LEAVING MARKETLINK
=======================
None.

ASP.NET sends the selected farmer/product/market history only to:
127.0.0.1:8001

That is the user's own local machine.

The service does NOT send data to an external AI provider.
