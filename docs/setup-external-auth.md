# External Authentication Setup

This guide explains how to obtain OAuth credentials for each supported external login provider.

## Microsoft

1. Go to [Azure Portal](https://portal.azure.com) > Azure Active Directory > App registrations > New registration
2. Name: "Ovcina Registrace"
3. Supported account types: "Accounts in any organizational directory and personal Microsoft accounts"
4. Redirect URI: Web
   - Dev: `https://localhost:7272/signin-microsoft`
   - Prod: `https://registrace.ovcina.cz/signin-microsoft`
5. After creation: copy **Application (client) ID** > put in `ExternalAuth:Microsoft:ClientId`
6. Go to Certificates & secrets > New client secret > copy value > put in `ExternalAuth:Microsoft:ClientSecret`

## Google

1. Go to [Google Cloud Console](https://console.cloud.google.com) > Create project (or select existing)
2. APIs & Services > Credentials > Create Credentials > OAuth 2.0 Client ID
3. Application type: Web application
4. Authorized redirect URIs:
   - Dev: `https://localhost:7272/signin-google`
   - Prod: `https://registrace.ovcina.cz/signin-google`
5. Copy Client ID > `ExternalAuth:Google:ClientId`
6. Copy Client Secret > `ExternalAuth:Google:ClientSecret`
7. Enable the "People API" in APIs & Services > Library

## Seznam

1. Go to [Seznam Developer Portal](https://vyvojari.seznam.cz)
2. Register a new application
3. Set OAuth 2.0 redirect URI:
   - Dev: `https://localhost:7272/signin-seznam`
   - Prod: `https://registrace.ovcina.cz/signin-seznam`
4. Copy Client ID > `ExternalAuth:Seznam:ClientId`
5. Copy Client Secret > `ExternalAuth:Seznam:ClientSecret`
6. Scopes needed: `openid`, `email`, `profile`

## Local Development with User Secrets

Store credentials securely using .NET user secrets (never commit real credentials):

```bash
cd src/RegistraceOvcina.Web
dotnet user-secrets set "ExternalAuth:Microsoft:ClientId" "your-client-id"
dotnet user-secrets set "ExternalAuth:Microsoft:ClientSecret" "your-secret"
dotnet user-secrets set "ExternalAuth:Google:ClientId" "your-client-id"
dotnet user-secrets set "ExternalAuth:Google:ClientSecret" "your-secret"
dotnet user-secrets set "ExternalAuth:Seznam:ClientId" "your-client-id"
dotnet user-secrets set "ExternalAuth:Seznam:ClientSecret" "your-secret"
```

## How It Works

Each provider is opt-in. If both `ClientId` and `ClientSecret` are non-empty in configuration, the provider is registered. If either value is empty (the default in `appsettings.json`), the provider is skipped.

The `ExternalLoginPicker` component dynamically queries registered external authentication schemes and renders a button for each one. When no providers are configured, it shows a placeholder message.

New users who register via an external provider are automatically assigned the **Registrant** role.
