# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props BiteBox.sln ./
COPY src/BiteBox.Domain/BiteBox.Domain.csproj src/BiteBox.Domain/
COPY src/BiteBox.Application/BiteBox.Application.csproj src/BiteBox.Application/
COPY src/BiteBox.Infrastructure/BiteBox.Infrastructure.csproj src/BiteBox.Infrastructure/
COPY src/BiteBox.API/BiteBox.API.csproj src/BiteBox.API/
RUN dotnet restore src/BiteBox.API/BiteBox.API.csproj
COPY src/ src/
RUN dotnet publish src/BiteBox.API/BiteBox.API.csproj -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app .
RUN mkdir -p /app/wwwroot/uploads && chown -R app:app /app/wwwroot
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BiteBox.API.dll"]
