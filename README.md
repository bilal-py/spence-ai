# Spence AI 🤖💰

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET Core](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/en-us/)
[![React](https://img.shields.io/badge/React-19.0-blue.svg)](https://react.dev/)
[![Database](https://img.shields.io/badge/Database-PostgreSQL-blue.svg)](https://neon.tech/)

Spence AI is a production-ready, open-source personal finance assistant. By utilizing a zero-cost infrastructure footprint, it bridges the gap between structured tracking and intelligent automation. Users can log their finances manually or drop a credit card statement PDF directly into the application to have it automatically parsed and categorized using Large Language Models.

---

## ✨ Features

* **Intelligent PDF Upload:** Seamlessly drag-and-drop credit card statements to extract raw data and map them into financial transactions.
* **AI Auto-Categorization:** Leverages the Google Gemini API to analyze transaction descriptions, cross-reference them with existing categories, or intuitively create logical new ones.
* **Multi-Dimensional Filtering:** Real-time query execution engine that filters your data concurrently by Year, Month, and Category.
* **Dynamic Financial Dashboard:** Beautiful, responsive visual breakdowns of monthly expenditure structures using interactive charts.
* **Token-Based Security:** Robust security layer built using JWT authentication workflows to completely isolate private financial workspaces.

---

## 🛠️ Tech Stack & Infrastructure

Spence AI is explicitly built to run on modern, production-grade cloud tiers that require **no subscription costs and no credit card inputs** for local development or basic hosting:

* **Frontend Web App:** React 19 + Tailwind CSS (Hosted Free on **Vercel**)
* **Backend REST API:** C# .NET 10 Web API (Containerized & Hosted Free on **Render**)
* **Serverless Database:** PostgreSQL (Hosted Free Tier on **Neon.tech**)
* **PDF Parsing Engine:** `PdfPig` (Open-source text-extraction NuGet utility)
* **Core intelligence:** Google Gemini API (Free developer tier via **Google AI Studio**)

---

## 📐 Architecture Overview

The backend system is designed using **Clean Architecture** patterns to enforce complete decoupling of business definitions from database frameworks and third-party AI drivers. 

```text
       ┌─────────────────────────────────────────────────────────┐
       │                   SpenceAI.WebApi                       │
       └────────────────────────────┬────────────────────────────┘
                                    │
       ┌────────────────────────────▼────────────────────────────┐
       │                 SpenceAI.Infrastructure                 │
       └────────────────────────────┬────────────────────────────┘
                                    │
       ┌────────────────────────────▼────────────────────────────┐
       │                  SpenceAI.Application                   │
       └────────────────────────────┬────────────────────────────┘
                                    │
       ┌────────────────────────────▼────────────────────────────┐
       │                    SpenceAI.Domain                      │
       └─────────────────────────────────────────────────────────┘