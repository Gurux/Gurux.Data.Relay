FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Development/ Development/
WORKDIR /src/Development
RUN dotnet publish Gurux.Data.Relay.Web/Server/Gurux.Data.Relay.Web.Server.csproj \
    -c Release -o /out \
    -p:GuruxUseLinuxDb2=true \
    -p:GeneratePackageOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out/ ./
RUN mkdir -p /data && chown app:app /data
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Gurux.Data.Relay.Web.Server.dll", "--settings-file", "/data/gurux.data.relay.settings.json"]