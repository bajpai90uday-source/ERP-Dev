# Authentication and Role-Based Access Control

## Local API setup

The API host is `src/backend/Erp.Api`. It requires an already configured PostgreSQL database and the `AuthenticationAndRbac` migration. It does not create or migrate a database automatically.

Configure the connection string and one-time bootstrap secret in the local environment or secret store (never in source control):

```powershell
$env:ConnectionStrings__Erp = '<PostgreSQL connection string>'
$env:Security__BootstrapToken = '<unique secret of at least 32 characters>'
dotnet run --project src/backend/Erp.Api/Erp.Api.csproj --launch-profile Erp.Api
```

The launch profile serves HTTPS at `https://localhost:7238` and redirects HTTP to HTTPS. The bootstrap endpoint is not mapped unless the bootstrap secret is configured. After initial setup, remove that secret from the runtime environment and restart the API.

## First-company bootstrap

Fetch an antiforgery request token from `GET /api/v1/auth/csrf`, retain its cookie, and send the returned `requestToken` in `X-ERP-CSRF` on the bootstrap POST. Send the bootstrap secret separately in `X-ERP-Bootstrap-Token`.

`POST /api/v1/setup/bootstrap` accepts:

```json
{
  "companyCode": "ACME",
  "companyName": "Example Manufacturing",
  "adminEmail": "administrator@example.invalid",
  "adminDisplayName": "ERP Administrator",
  "adminPassword": "Bbbbbbbbb78$"
}
```

Bootstrap is rate-limited, can only create the first user, and returns a conflict if another setup has already created a user. It creates the initial company-scoped roles and global permission catalog in the same transaction as the first Administration account. It never uses a built-in password. `Administration` receives every permission; the Sales, Purchase, Stores, Production, Quality, Maintenance, HR, Dispatch, and Finance roles receive their initial module/action grants.

## Sign-in and browser sessions

All mutating `/api` requests, including login and logout, require a valid antiforgery token in `X-ERP-CSRF`. Fetch a fresh request token from the CSRF endpoint when needed; the paired antiforgery cookie is HttpOnly.

`POST /api/v1/auth/login` takes the email, password, and company ID:

```json
{
  "email": "administrator@example.invalid",
  "password": "<account password>",
  "companyId": "<company UUID>"
}
```

A successful login sets a nonpersistent, 30-minute sliding session cookie. The cookie is host-only, Secure, HttpOnly, SameSite=Strict, and named with the `__Host-` prefix. Login is rate-limited per client IP; after five failed password attempts, the account is locked for 15 minutes. Invalid login responses do not disclose whether the email, password, or company membership was incorrect.

- `GET /api/v1/auth/me` returns the authenticated user and active company.
- `POST /api/v1/auth/logout` invalidates the cookie and records an audit event.
- `GET /health` is the only non-authentication API health route.

Passwords are hashed with ASP.NET Core's `PasswordHasher<UserAccount>`; the API does not store or log plaintext passwords. New and reset passwords require at least 12 characters (maximum 1024).

## Users, roles, and permissions

All `/api/v1/admin` routes require authentication and the relevant company-scoped permission. The API fallback policy also requires authentication for any future endpoint unless that endpoint is deliberately marked anonymous.

| Endpoint | Required permission |
|---|---|
| `GET /api/v1/admin/users` | `security.users.read` |
| `POST /api/v1/admin/users` | `security.users.create` |
| `PUT /api/v1/admin/users/{userId}/roles` | `security.users.update` |
| `PUT /api/v1/admin/users/{userId}/active` | `security.users.deactivate` |
| `POST /api/v1/admin/users/{userId}/password` | `security.users.reset_password` |
| `GET /api/v1/admin/roles` | `security.roles.read` |
| `POST /api/v1/admin/roles` | `security.roles.create` |
| `PUT /api/v1/admin/roles/{roleId}/permissions` | `security.roles.update` |
| `GET /api/v1/admin/permissions` | `security.permissions.read` |
| `GET /api/v1/admin/security-audit` | `security.audit.read` |

Role codes and permission codes are returned by the corresponding GET endpoints. A user can only be assigned roles belonging to the signed-in company. A permission such as `production.confirm` is effective only when the user has both `production.access` and `production.confirm`, through one or more active company roles. Permission changes are read from the database during API authorization; hiding a UI control is not security enforcement.

The API prevents deactivating or removing the Administration role from the last active Administration user in a company. User/role changes and sign-in success/failure/logout/bootstrap events are written to `SecurityAuditEvents`; audit payloads exclude password material and bootstrap/session tokens.

## Validation

Run the backend tests with:

```powershell
dotnet test tests/backend/Erp.Infrastructure.Tests/Erp.Infrastructure.Tests.csproj
```

The API integration tests use SQLite in-memory for automated access-control behavior. PostgreSQL migration application and production deployment still require a configured PostgreSQL instance; SQLite test success does not replace PostgreSQL integration testing.
