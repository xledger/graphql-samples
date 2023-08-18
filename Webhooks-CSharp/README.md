# Webhooks C#

# Running Instructions:

1. Copy config.json.example to config.json
2. Edit the config, adding your GraphQL token, and changing the graphql endpoint if needed.
  - For example, if you are testing this against the demo environment, change that graphQLEndpoint to: `https://demo.xledger.net/graphql`
3. Initialize the sqlite database by running this command:

```powershell
dotnet run -- db:migrate config.json
```

4. Start synchronizing your projects by running this command:

```powershell
dotnet run -- projects:sync config.json
```