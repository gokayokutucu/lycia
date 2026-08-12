FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
ARG SERVICE
RUN dotnet publish samples/Microservices/${SERVICE}/${SERVICE}.csproj -c Release -o /app --nologo
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
ARG SERVICE
ENV SERVICE_DLL=${SERVICE}.dll
COPY --from=build /app .
ENTRYPOINT ["sh","-c","dotnet ${SERVICE_DLL}"]
