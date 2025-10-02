#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app

RUN adduser -u 5679 --disabled-password --gecos "" burganazuredevopsaggregatoruser && chown -R burganazuredevopsaggregatoruser:burganazuredevopsaggregatoruser /app
USER burganazuredevopsaggregatoruser

FROM mcr.microsoft.com/dotnet/sdk:6.0-focal AS build
WORKDIR /src
COPY ["BurganAzureDevopsAggregator.csproj", "."]
RUN dotnet restore "./BurganAzureDevopsAggregator.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "BurganAzureDevopsAggregator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BurganAzureDevopsAggregator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN chown -R 5679:5679 /tmp \
    && chmod -R u+rwX,g+rwX /tmp
    
EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
ENTRYPOINT ["dotnet", "BurganAzureDevopsAggregator.dll"]