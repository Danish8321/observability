Status: open

# Screening.Worker: CouchDB calls 401 — same userinfo/BaseAddress bug as the API

`samples/Screening.Worker/Program.cs:27-28` builds `ApplicationRepository`'s
`HttpClient` the same way `Screening.Api` used to:

```csharp
builder.Services.AddHttpClient<ApplicationRepository>(client =>
    client.BaseAddress = new Uri(couchDbUrl));
```

`couchDbUrl` carries `admin:password@` as URI userinfo. `HttpClient` ignores
userinfo silently (does not send Basic auth), so every CouchDB call from the
worker will 401. Identical root cause to issue 01's first blocker, same fix
already applied in `Screening.Api/Program.cs` — port it here.

## Why this wasn't caught during issue 01

Issue 01's verification only exercised `Screening.Api`'s own CouchDB calls
(the `GET`/`PUT` around `POST /applications`); the worker was never invoked
because the NATS publish step fails first (see issue 03), so the worker's
consumer loop never got a message to try processing.

## What to do

Apply the same fix as `samples/Screening.Api/Program.cs`: strip userinfo from
`BaseAddress`, set `Authorization: Basic …` explicitly. Then re-run the full
happy path (`app-1001` through both services) once issue 03 is also fixed, to
confirm the worker's CouchDB reads/writes succeed.

## Comments
