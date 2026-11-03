# Finish GitHub and Azure setup

Run commands from `D:\University\Year-4\Cloud Computing\SSP-1`. The app creates 50 images using the available Buienradar stations; when 39 are available, 11 are reused with unchanged readings. This is not 50 distinct stations: obtain assessor agreement. No account setup or deployment has been performed by these instructions.

## 1. Publish the repository

On GitHub, create an **empty** repository named `weather-images` (do not initialize a README or .gitignore). Then run the following, replacing YOUR-USERNAME. Git may request your name/email or browser sign-in. Never use your GitHub account password in a command.

```powershell
git init -b main
git add .
git status --short
git commit -m "Implement queued weather image generation"
git remote add origin https://github.com/YOUR-USERNAME/weather-images.git
git push -u origin main
```

The existing `.gitignore` excludes local settings, build outputs, emulator data, Azure CLI data and `deployment.private.json`. Before committing, confirm no credentials are staged. Do not run `git init`/`remote add` again if you already initialized or connected a repository.

In repository **Settings → Collaborators → Add people**, invite **Hijdra**. If using a course organization/project, also provide the required organization/project access. Check that the invitation is accepted before submission.

## 2. Deploy once from your computer

Prerequisites: Azure CLI, .NET 8 runtime and SDK 8 or later, and an Azure subscription with permission to create resources. Use your university/student subscription if available. The deployment creates billable resources. It uses a dedicated resource group.

```powershell
az login
az account list --query "[].{Name:name,Id:id}" -o table
az account set --subscription "YOUR-SUBSCRIPTION-ID"
az bicep install
az provider register --namespace Microsoft.Web --wait
az provider register --namespace Microsoft.Storage --wait
az provider register --namespace Microsoft.Insights --wait
.\deploy.ps1 -ResourceGroup rg-weather-images -Location westeurope
```

The script publishes .NET code, checks the zip layout, deploys Bicep, uploads the app, waits for health, verifies that missing/incorrect keys return 401, then creates and checks 50 image results. Allow several minutes. Do not call deployment successful unless it exits successfully. If Azure rejects a region or quota, use an allowed region or request quota from your subscription administrator.

On success:

- `deployment.private.json` contains the URL and function key. Keep this file private and share the key privately with the assessor.
- `docs/api.deployed.http` contains the real public URL and a completed run ID; the key remains a placeholder.
- `docs/deployment-verification.json` contains non-secret verification evidence.

To repeat cloud verification later without displaying the key:

```powershell
$deployment = Get-Content deployment.private.json -Raw | ConvertFrom-Json
.\scripts\smoke.ps1 -BaseUrl $deployment.baseUrl -RequestKey $deployment.requestKey -VerifyAuthentication
```

Commit public deployment evidence:

```powershell
git add docs/api.deployed.http docs/deployment-verification.json
git commit -m "Document verified Azure endpoint"
git push
```

## 3. Enable GitHub automatic deployment

Use OpenID Connect (OIDC), with no client password. You need permission to create an Entra app and assign a role. University tenants may restrict these actions; ask their administrator if those options are unavailable.

1. In Azure portal → **Microsoft Entra ID → App registrations → New registration**, create `weather-images-github`, single tenant, no redirect URI. Record **Application (client) ID** and **Directory (tenant) ID**.
2. Open that registration → **Certificates & secrets → Federated credentials → Add credential**. Choose **GitHub Actions deploying Azure resources**. Enter your GitHub owner and repository name, choose entity **Environment**, and enter **azure** exactly. The issuer is `https://token.actions.githubusercontent.com`, subject `repo:YOUR-USERNAME/weather-images:environment:azure`, and audience `api://AzureADTokenExchange`.
3. In Azure → resource group **rg-weather-images → Access control (IAM) → Add role assignment**, give this app/service principal **Contributor**, scoped to this resource group. Assign access to a **User, group, or service principal**, then select the registered app. This supports resource deployment and listing the function/storage keys used by this project. No subscription-wide role is needed when the resource group already exists.
4. In GitHub → repository **Settings → Environments**, create the environment **azure**. Optionally limit it to the `main` branch.
5. Inside that environment, add these **secrets**:

   | Secret | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | Application (client) ID |
   | `AZURE_TENANT_ID` | Directory (tenant) ID |
   | `AZURE_SUBSCRIPTION_ID` | Your selected subscription ID |

6. In the same environment, add variable `AZURE_RESOURCE_GROUP` = `rg-weather-images`.
7. In repository **Settings → Secrets and variables → Actions → Variables**, add repository variable `ENABLE_AZURE_DEPLOY` = `true`. It must be a repository variable because the job condition is evaluated before environment variables are available.
8. In **Actions → Build and deploy → Run workflow**, run the `main` branch once. Both jobs must pass. Future pushes to `main` build, test, provision/deploy, and verify cloud authentication and 50 images automatically.

The workflow uploads only **public-deployment-evidence**, containing public HTTP examples and verification JSON. Download it from the successful Actions run and copy those files into `docs` if you need the latest public examples. It never uploads `deployment.private.json`. Retrieve your key locally with Azure CLI if it changes; do not print it in public logs.

## 4. Submit

Use `docs/submission.md`. Include the repository URL, public API URL, private function key, name **Naseer Ahmed Arsalan**, student number **722589**, and truthful AI disclosure. Describe the repeated-station approach. Send the email yourself after cloud verification and assessor access are complete.

Sources: [Microsoft OIDC setup](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect), [GitHub OIDC in Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure), [GitHub collaborator invitations](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/inviting-collaborators-to-a-personal-repository).
