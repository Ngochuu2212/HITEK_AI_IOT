# Build từ thư mục gốc HitekAiIot: docker build -f src/Hitek.SensorSimulator/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Hitek.SensorSimulator/Hitek.SensorSimulator.csproj src/Hitek.SensorSimulator/
RUN dotnet restore src/Hitek.SensorSimulator/Hitek.SensorSimulator.csproj
COPY src/Hitek.SensorSimulator/ src/Hitek.SensorSimulator/
RUN dotnet publish src/Hitek.SensorSimulator/Hitek.SensorSimulator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Hitek.SensorSimulator.dll"]
