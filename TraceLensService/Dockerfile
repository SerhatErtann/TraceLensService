# Derleme bağlamı repo kökü olmalı (nuget.config orada):
#   docker build -f TraceLensService/Dockerfile -t tracelens-service .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config .
COPY TraceLensService/TraceLensService.csproj TraceLensService/
RUN dotnet restore TraceLensService/TraceLensService.csproj
COPY TraceLensService/ TraceLensService/
RUN dotnet publish TraceLensService/TraceLensService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Image'lar .NET 8+ ile varsayılan olarak root olmayan "app" kullanıcısıyla ve 8080 portunda çalışır
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TraceLensService.dll"]
