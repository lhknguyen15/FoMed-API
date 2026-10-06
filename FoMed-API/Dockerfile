FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FoMed-API/FoMed.Infrastructure/FoMed.Infrastructure.csproj FoMed.Infrastructure/
COPY FoMed-API/FoMed.Application/FoMed.Application.csproj FoMed.Application/
COPY FoMed-API/FoMed.Api/FoMed.Api.csproj FoMed.Api/
RUN dotnet restore FoMed.Api/FoMed.Api.csproj
COPY FoMed-API/ ./
RUN dotnet publish FoMed.Api/FoMed.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false \
    && rm -f /out/appsettings.Development.json /out/appsettings.SePay.example.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
USER app
# Render supplies PORT. No credentials, database seed or migrations in the image.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet FoMed.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
