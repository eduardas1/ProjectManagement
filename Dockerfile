FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /source

COPY global.json Directory.Build.props Workboard.sln ./
COPY src/Workboard.Domain/Workboard.Domain.csproj src/Workboard.Domain/packages.lock.json src/Workboard.Domain/
COPY src/Workboard.Application/Workboard.Application.csproj src/Workboard.Application/packages.lock.json src/Workboard.Application/
COPY src/Workboard.Infrastructure/Workboard.Infrastructure.csproj src/Workboard.Infrastructure/packages.lock.json src/Workboard.Infrastructure/
COPY src/Workboard.Web/Workboard.Web.csproj src/Workboard.Web/packages.lock.json src/Workboard.Web/
RUN dotnet restore Workboard.sln --locked-mode

COPY . .
RUN dotnet publish src/Workboard.Web/Workboard.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
RUN mkdir -p /data/keys && chown -R app:app /data
ENV Storage__Directory=/data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Workboard.Web.dll"]
