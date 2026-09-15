FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /source

COPY global.json Directory.Build.props ProjectManagement.sln ./
COPY src/ProjectManagement.Domain/ProjectManagement.Domain.csproj src/ProjectManagement.Domain/packages.lock.json src/ProjectManagement.Domain/
COPY src/ProjectManagement.Application/ProjectManagement.Application.csproj src/ProjectManagement.Application/packages.lock.json src/ProjectManagement.Application/
COPY src/ProjectManagement.Infrastructure/ProjectManagement.Infrastructure.csproj src/ProjectManagement.Infrastructure/packages.lock.json src/ProjectManagement.Infrastructure/
COPY src/ProjectManagement.Web/ProjectManagement.Web.csproj src/ProjectManagement.Web/packages.lock.json src/ProjectManagement.Web/
COPY tests/ProjectManagement.Tests/ProjectManagement.Tests.csproj tests/ProjectManagement.Tests/packages.lock.json tests/ProjectManagement.Tests/
RUN dotnet restore ProjectManagement.sln --locked-mode

COPY . .
RUN dotnet publish src/ProjectManagement.Web/ProjectManagement.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
RUN mkdir -p /data/keys && chown -R app:app /data
ENV Storage__Directory=/data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjectManagement.Web.dll"]
