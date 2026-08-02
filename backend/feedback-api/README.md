# PC-SPA feedback API

Backend-only foundation for voluntary, user-reviewed PC-SPA beta error reports.

## Privacy and security boundaries

- The desktop application remains fully functional offline.
- This service accepts only explicit `POST /v1/feedback` requests.
- No personal files, file contents, credentials, cookies, licence keys, or unrelated process data are accepted by the schema.
- Common email addresses and Windows personal paths are redacted again server-side.
- The anonymous installation identifier is salted and hashed before storage.
- Client IP addresses are used transiently for rate limiting and are not stored in D1.
- Reports expire after 45 days and are deleted by a daily scheduled task.
- There is no public report-reading endpoint.

Free-form user descriptions can contain information voluntarily typed by the user. The desktop application must display the exact report and obtain consent before a future submission sprint connects to this API.

## Local verification

```powershell
cd backend\feedback-api
npm test
```

## Deployment prerequisites (not part of Sprint 37B)

1. Create a Cloudflare D1 database.
2. Replace `REPLACE_WITH_D1_DATABASE_ID` in `wrangler.jsonc`.
3. Create a random secret of at least 32 characters:
   `npx wrangler secret put FEEDBACK_HASH_SALT`
4. Apply the D1 migration.
5. Run local and deployment verification before connecting PC-SPA.

Never commit Cloudflare credentials, API tokens, or the hashing salt.
