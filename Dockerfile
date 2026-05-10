FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FeatureFlags.slnx ./
COPY src/featureflags/FeatureFlags.csproj src/featureflags/
COPY tests/FeatureFlags.Tests/FeatureFlags.Tests.csproj tests/FeatureFlags.Tests/
RUN dotnet restore src/featureflags/FeatureFlags.csproj

COPY . .
RUN dotnet publish src/featureflags/FeatureFlags.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__FeatureFlags="Data Source=/data/featureflags.db"

RUN mkdir -p /data
VOLUME ["/data"]

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "FeatureFlags.dll"]
