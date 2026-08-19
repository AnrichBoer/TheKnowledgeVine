# Local review

All website work is on the `codex` branch. The existing `master` branch has not been changed.

## 1. Start the API

The previous SMTP password was removed from tracked configuration because it was committed to Git. Rotate that credential with the mail provider before using the form again.

In a new PowerShell window, set the rotated password only for that terminal session and start the API:

```powershell
$env:EmailSettings__Password = '<your newly rotated SMTP password>'
dotnet run --project .\KnowledgeVineAPI\KnowledgeVineAPI\KnowledgeVineAPI.csproj --launch-profile http
```

The API listens on `http://localhost:5222` with that profile.

## 2. Start the Angular site

In a second PowerShell window:

```powershell
Set-Location .\knowledevinefront
npm start
```

Open `http://localhost:4200`. Angular proxies `/api` requests to the local API, so the enquiry form can be tested without changing production URLs.

## 3. Production configuration

`deploy/nginx/knowledgevine.conf.example` contains the canonical-host redirect, SPA fallback, API proxy, compression, caching and security-header configuration. Review its certificate paths, frontend root and API port against the server before applying it.
