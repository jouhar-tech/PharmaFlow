# PharmaFlow

## Local authentication configuration

Set these values using .NET User Secrets (never commit them):

```powershell
dotnet user-secrets set "Supabase:Url" "https://YOUR_PROJECT.supabase.co"
dotnet user-secrets set "Supabase:AnonKey" "YOUR_SUPABASE_ANON_KEY"
```

Enable Phone provider and configure an SMS provider in Supabase Authentication settings.

For Google login, enable Google as an OAuth provider in Supabase and add the application callback URL to the allowed redirect URLs. The current implementation includes the authorization redirect; secure server-side token exchange must be completed before production use.

Run locally:

```powershell
dotnet build
dotnet run
```
