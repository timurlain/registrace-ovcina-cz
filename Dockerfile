FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "."]
COPY ["src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj", "src/RegistraceOvcina.Web/"]

RUN dotnet restore "src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj"

COPY . .

RUN dotnet publish "src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "RegistraceOvcina.Web.dll"]
