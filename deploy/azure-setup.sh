#!/usr/bin/env bash
# Creates PlateBack's Azure resources on free tiers (Week 6). These are the
# commands that built the live site, in order; docs/deployment-guide.md in the
# frontend repository explains each choice and what can go wrong.
#
#   az login --use-device-code
#   NAME=rahul REGION=uaenorth deploy/azure-setup.sh
#
# Secrets are generated here and written only to ~/.config/plateback/azure.env
# (readable by you alone) and to the App Service's App settings.
# Delete everything afterwards with: az group delete --name rg-plateback --yes
set -euo pipefail

NAME=${NAME:?Set NAME to a short unique suffix, for example your name}
REGION=${REGION:-uaenorth}
RG=${RG:-rg-plateback}
DB=plateback-db-$NAME
STORAGE=platebackphotos$NAME          # lowercase letters and digits only
APP=plateback-$NAME                   # becomes https://$APP.azurewebsites.net
SECRETS=~/.config/plateback/azure.env

# 1. Services a new subscription has not switched on yet (free to register).
#    Without this, creating a storage account fails with a misleading
#    "SubscriptionNotFound".
for ns in Microsoft.DBforPostgreSQL Microsoft.Storage Microsoft.Web Microsoft.Insights \
          Microsoft.OperationalInsights Microsoft.AlertsManagement; do
  az provider register --namespace "$ns" --wait
done

# 2. Secrets, generated locally.
mkdir -p ~/.config/plateback && chmod 700 ~/.config/plateback
if [ ! -f "$SECRETS" ]; then
  umask 077
  {
    echo "DB_ADMIN_USER=platebackadmin"
    echo "DB_ADMIN_PASSWORD=$(openssl rand -base64 30 | tr -d '/+=' | cut -c1-28)Aa1"
    echo "JWT_KEY=$(openssl rand -base64 48 | tr -d '\n')"
    echo "DEMO_ADMIN_PASSWORD=$(openssl rand -base64 18 | tr -d '/+=')Aa1"
  } > "$SECRETS"
fi
# shellcheck disable=SC1090
source "$SECRETS"

# 3. Resource group: everything lives in it, so one command deletes it all.
az group create --name "$RG" --location "$REGION"

# 4. Database: Burstable B1ms with 32 GB is the size the free offer covers
#    (750 hours a month). The portal's default, Standard_B2s, is not free.
#    --public-access 0.0.0.0 means "Azure services only", no internet ranges.
az postgres flexible-server create \
  --resource-group "$RG" --name "$DB" --location "$REGION" \
  --tier Burstable --sku-name Standard_B1ms --storage-size 32 --version 16 \
  --zonal-resiliency Disabled --backup-retention 7 --geo-redundant-backup Disabled \
  --storage-auto-grow Disabled \
  --admin-user "$DB_ADMIN_USER" --admin-password "$DB_ADMIN_PASSWORD" \
  --public-access 0.0.0.0 --yes
az postgres flexible-server db create --resource-group "$RG" --server-name "$DB" --name plateback

# 5. Photo storage: private, locally redundant (GRS is the portal default and is not free).
az storage account create --name "$STORAGE" --resource-group "$RG" --location "$REGION" \
  --sku Standard_LRS --kind StorageV2 --access-tier Hot \
  --allow-blob-public-access false --min-tls-version TLS1_2 --https-only true
az storage container-rm create --storage-account "$STORAGE" --resource-group "$RG" \
  --name listing-photos --public-access off

# 6. App Service on the free F1 plan.
az appservice plan create --name plan-plateback --resource-group "$RG" --location "$REGION" --is-linux --sku F1
az webapp create --name "$APP" --resource-group "$RG" --plan plan-plateback \
  --runtime "DOTNETCORE:10.0" --https-only true
az webapp config set --name "$APP" --resource-group "$RG" \
  --ftps-state Disabled --min-tls-version 1.2 --web-sockets-enabled true --http20-enabled true

# 7. Managed identity, allowed into the photo container and nothing else.
az webapp identity assign --name "$APP" --resource-group "$RG"
principal=$(az webapp identity show --name "$APP" --resource-group "$RG" --query principalId -o tsv)
storage_id=$(az storage account show --name "$STORAGE" --resource-group "$RG" --query id -o tsv)
az role assignment create --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" \
  --scope "$storage_id/blobServices/default/containers/listing-photos"

# 8. Application Insights, with a 0.1 GB daily cap on its workspace so it
#    stays inside the 5 GB a month free allowance whatever happens.
az config set extension.use_dynamic_install=yes_without_prompt
az monitor log-analytics workspace create --resource-group "$RG" --workspace-name plateback-logs \
  --location "$REGION" --retention-time 30 --quota 0.1
workspace=$(az monitor log-analytics workspace show --resource-group "$RG" --workspace-name plateback-logs --query id -o tsv)
az monitor app-insights component create --app plateback-insights --resource-group "$RG" \
  --location "$REGION" --workspace "$workspace" --kind web --application-type web
insights=$(az monitor app-insights component show --app plateback-insights --resource-group "$RG" --query connectionString -o tsv)

# 9. App settings: every secret lives here, never in the repository.
az webapp config appsettings set --name "$APP" --resource-group "$RG" --output none --settings \
  "ASPNETCORE_ENVIRONMENT=Production" \
  "ConnectionStrings__Default=Host=$DB.postgres.database.azure.com;Port=5432;Database=plateback;Username=$DB_ADMIN_USER;Password=$DB_ADMIN_PASSWORD;SSL Mode=Require" \
  "Jwt__Key=$JWT_KEY" \
  "Demo__AdminPassword=$DEMO_ADMIN_PASSWORD" \
  "Storage__BlobServiceUri=https://$STORAGE.blob.core.windows.net" \
  "APPLICATIONINSIGHTS_CONNECTION_STRING=$insights"

# 10. Deploy credentials for GitHub Actions: publishing allowed, FTP not.
az resource update --resource-group "$RG" --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies --parent "sites/$APP" --name scm --set properties.allow=true
az resource update --resource-group "$RG" --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies --parent "sites/$APP" --name ftp --set properties.allow=false
(umask 077; az webapp deployment list-publishing-profiles --name "$APP" --resource-group "$RG" --xml \
  > ~/.config/plateback/publish-profile.xml)

# 11. Budget: an email if anything costs more than $0.01 in a month.
subscription=$(az account show --query id -o tsv)
email=$(az account show --query user.name -o tsv)
az rest --method put \
  --url "https://management.azure.com/subscriptions/$subscription/providers/Microsoft.Consumption/budgets/plateback-zero-cost?api-version=2023-05-01" \
  --body "{\"properties\":{\"category\":\"Cost\",\"amount\":1,\"timeGrain\":\"Monthly\",
    \"timePeriod\":{\"startDate\":\"$(date -u +%Y-%m-01)T00:00:00Z\",\"endDate\":\"$(date -u -d '+1 year' +%Y-%m-01)T00:00:00Z\"},
    \"notifications\":{\"anyCost\":{\"enabled\":true,\"operator\":\"GreaterThan\",\"threshold\":1,
    \"thresholdType\":\"Actual\",\"contactEmails\":[\"$email\"]}}}}"

echo
echo "Done. Next:"
echo "  1. GitHub, Plateback-Backend, Settings, Secrets and variables, Actions:"
echo "     secret AZURE_WEBAPP_PUBLISH_PROFILE = contents of ~/.config/plateback/publish-profile.xml"
echo "     variable AZURE_WEBAPP_NAME = $APP"
echo "  2. Push to main (or run the Deploy workflow); the site is https://$APP.azurewebsites.net"
