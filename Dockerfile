FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish src/OnChainReader.Api/OnChainReader.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# ETH_RPC_URL must be provided at run time (it usually contains an API key): never bake it into the image.
USER app
ENTRYPOINT ["dotnet", "OnChainReader.Api.dll"]
