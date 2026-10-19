# Multi-stage container build for PharmaFlow (.NET 10 ASP.NET Core MVC).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["PharmaFlow/PharmaFlow.csproj", "PharmaFlow/"]
RUN dotnet restore "PharmaFlow/PharmaFlow.csproj"

COPY . .
WORKDIR /src/PharmaFlow
RUN dotnet publish "PharmaFlow.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Render's default web-service port is 10000. Keep the app bound to all interfaces.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 10000

ENTRYPOINT ["dotnet", "PharmaFlow.dll"]
