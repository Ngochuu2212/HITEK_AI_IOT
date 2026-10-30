# Build từ thư mục gốc HitekAiIot: docker build -f src/Hitek.IotService/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Hitek.IotService/Hitek.IotService.csproj src/Hitek.IotService/
RUN dotnet restore src/Hitek.IotService/Hitek.IotService.csproj
COPY src/Hitek.IotService/ src/Hitek.IotService/
RUN dotnet publish src/Hitek.IotService/Hitek.IotService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hitek.IotService.dll"]
