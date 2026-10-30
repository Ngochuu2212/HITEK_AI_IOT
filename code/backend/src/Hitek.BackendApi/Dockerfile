# Build từ thư mục gốc HitekAiIot: docker build -f src/Hitek.BackendApi/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Hitek.BackendApi/Hitek.BackendApi.csproj src/Hitek.BackendApi/
RUN dotnet restore src/Hitek.BackendApi/Hitek.BackendApi.csproj
COPY src/Hitek.BackendApi/ src/Hitek.BackendApi/
RUN dotnet publish src/Hitek.BackendApi/Hitek.BackendApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hitek.BackendApi.dll"]
