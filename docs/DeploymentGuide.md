# Deployment Guide — SAML Sample App

This guide covers deploying the app to Azure App Service, retrieving bootstrap credentials, configuring SAML SSO, and disabling the bootstrap admin account.

---

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed and signed in (`az login`)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed locally
- An Azure subscription

---

## 1. Create Azure Resources

```powershell
# Create a resource group
az group create --name rg-saml-sample --location eastus

# Create an App Service plan (F1 = free tier)
az appservice plan create `
  --name plan-saml-sample `
  --resource-group rg-saml-sample `
  --sku F1

# Create the web app (.NET 10)
az webapp create `
  --name <your-app-name> `
  --resource-group rg-saml-sample `
  --plan plan-saml-sample `
  --runtime "dotnet:10"
```

Replace `<your-app-name>` with a globally unique name — this becomes `<your-app-name>.azurewebsites.net`.

---

## 2. Configure App Settings

Set the `AllowedHosts` setting to lock the app to its actual hostname:

In the **Azure Portal**:
1. Go to **App Services → \<your-app-name\> → Configuration → Application settings**
2. Add: `AllowedHosts` = `<your-app-name>.azurewebsites.net`
3. Click **Save**

> You can also do this via the CLI — but due to a known quoting issue with PowerShell and the Azure CLI on Windows, the portal is more reliable for this step.

---

## 3. Build and Deploy

Run the following from a local PowerShell terminal in the project directory:

```powershell
cd "Saml Sample App/SamlSample.Web"

dotnet publish -c Release -o ./publish

Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force

az webapp deploy `
  --name <your-app-name> `
  --resource-group rg-saml-sample `
  --src-path ./publish.zip `
  --type zip
```

The app will start automatically. On first startup it creates the SQLite database and generates bootstrap admin credentials.

---

## 4. Enable Bootstrap Admin (First Login Only)

The bootstrap admin login is **off by default in Production**. Enable it temporarily via an app setting:

1. Go to **Azure Portal → App Services → \<your-app-name\> → Configuration → Application settings**
2. Add: `BootstrapAdminEnabled` = `true`
3. Click **Save** — the app restarts automatically

> **Security note:** Remove this setting once you have SAML configured and have disabled the bootstrap account (Step 7).

---

## 5. Get Bootstrap Admin Credentials

On first startup, the app writes credentials to a file inside the deployment. Retrieve them via the Kudu debug console:

1. Open: `https://<your-app-name>.scm.azurewebsites.net/DebugConsole`
2. In the file browser at the top, click: **site → wwwroot → .internal**
3. Click the download icon next to `bootstrap-admin-credentials.txt`

Or run this in the Kudu terminal at the bottom of that page:
```
type site\wwwroot\.internal\bootstrap-admin-credentials.txt
```

Alternatively, paste this URL directly in your browser (authenticates with Azure credentials):
```
https://<your-app-name>.scm.azurewebsites.net/api/vfs/site/wwwroot/.internal/bootstrap-admin-credentials.txt
```

---

## 6. Configure SAML SSO

1. Navigate to `https://<your-app-name>.azurewebsites.net`
2. Click **Bootstrap Admin Login** and sign in with the credentials from Step 5
3. Go to **Admin → SSO Configuration**
4. Click the app you want to configure (e.g. "SAML Launcher") or create a new one
5. Fill in the fields:

   | Field | Value |
   |-------|-------|
   | **Assertion Consumer Service URL** | Pre-filled as `https://<your-app-name>.azurewebsites.net/saml/<slug>/acs` — confirm it, then copy it |
   | **SP Entity ID** | A unique URI to identify this service provider (e.g. `https://<your-app-name>.azurewebsites.net`) |
   | **Federation Metadata URL** | Your IdP's federation metadata URL (e.g. Entra ID's `federationmetadata.xml?appid=...` URL) |

6. Click **Import Metadata** to auto-populate IdP details and signing certificates from the metadata URL
7. Click **Save**

### Register the ACS URL in your IdP (Entra ID example)

In **Entra ID → App registrations → \<your app\>**:
- Set the **Redirect URI** (SAML ACS URL) to: `https://<your-app-name>.azurewebsites.net/saml/<slug>/acs`
- Ensure the app has the correct claims configured (at minimum: `role` claim)

---

## 7. Verify SSO Login

1. Sign out of the bootstrap admin session
2. Navigate to `https://<your-app-name>.azurewebsites.net`
3. Click the SSO login link — you should be redirected to your IdP and back

---

## 8. Disable Bootstrap Admin

Once SSO is working:

1. Sign in via SAML as an admin
2. Go to **Admin → App Controls**
3. Click **Disable Bootstrap Admin Login**

Then remove the temporary app setting:

1. Go to **Azure Portal → App Services → \<your-app-name\> → Configuration → Application settings**
2. Delete the `BootstrapAdminEnabled` setting
3. Click **Save**

The bootstrap login endpoint will now be fully inaccessible.

---

## Re-deploying After Code Changes

```powershell
cd "Saml Sample App/SamlSample.Web"

dotnet publish -c Release -o ./publish

Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force

az webapp deploy `
  --name <your-app-name> `
  --resource-group rg-saml-sample `
  --src-path ./publish.zip `
  --type zip
```

> **Note:** The SQLite database (`samlsample.db`) persists in the App Service file system between deployments — SAML configuration is not lost on redeploy.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Bootstrap login link not visible | Check that `BootstrapAdminEnabled=true` is set in App Settings and the app has restarted |
| Wrong credentials | Retrieve fresh credentials from Kudu (Step 5) — Azure generates different credentials than local |
| SAML login fails after SSO setup | Verify the ACS URL in the IdP matches exactly what's in the app's SSO configuration |
| App crashes on startup | Check App Service logs: **Azure Portal → App Services → Log stream** |
| `AllowedHosts` error in logs | Ensure the `AllowedHosts` app setting matches the actual hostname |
