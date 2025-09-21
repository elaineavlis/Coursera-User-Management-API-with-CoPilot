# Coursera-User-Management-API-with-CoPilot
UserManagementAPI is a lightweight and secure RESTful API built with ASP.NET Core Minimal APIs. It provides full JWT-based authentication and authorization, robust error handling, and request/response logging.

---

## 🚀 Features

- 🔐 JWT authentication and authorization
- 📋 Full user management (CRUD)
- 🧪 Input validation for user data
- 📦 Optional paging for user queries
- 🧠 Global error handling
- 📊 Request/response logging middleware
- ⚙️ Minimal API architecture (no MVC)

---

## 📦 Endpoints

All endpoints require JWT authorization (`RequireAuthorization()`).

### GET `/users?skip={int}&take={int}`
- Returns a list of users
- Supports optional paging via `skip` and `take`

### GET `/users/{id}`
- Returns a user by ID

### POST `/users`
- Adds a new user
- Body: `{ "username": "...", "email": "..." }`

### PUT `/users/{id}`
- Updates an existing user

### DELETE `/users/{id}`
- Deletes a user by ID

---

## 🛡️ Authentication

This API uses JWT (JSON Web Tokens). To access protected endpoints:

1. Generate a token (handled externally — login system not included)
2. Send the token in the `Authorization` header:

```http
Authorization: Bearer <your-token>

