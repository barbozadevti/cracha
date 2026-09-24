# Imagem de produção do Crachá: compila com o SDK e roda só com o runtime do ASP.NET.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/Cracha.Api/Cracha.Api.csproj src/Cracha.Api/
RUN dotnet restore src/Cracha.Api/Cracha.Api.csproj
COPY src/Cracha.Api/ src/Cracha.Api/
RUN dotnet publish src/Cracha.Api/Cracha.Api.csproj --configuration Release --output /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
# Pasta de dados gravável pelo usuário sem privilégios (SQLite e fotos no modo local).
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app/App_Data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cracha.Api.dll"]
