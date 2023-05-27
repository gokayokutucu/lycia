#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 6000
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["/samples/Sample.Consumer/Sample.Consumer.csproj", "samples/Sample.Consumer/"]
COPY ["/src/Lycia.Dapr/Lycia.Dapr.csproj", "src/Lycia.Dapr/"]
RUN dotnet restore "samples/Sample.Consumer/Sample.Consumer.csproj"
COPY . .
WORKDIR "/src/samples/Sample.Consumer"
RUN dotnet build "Sample.Consumer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sample.Consumer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sample.Consumer.dll"]