FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Company.Logging.Abstractions/Company.Logging.Abstractions.csproj", "Company.Logging.Abstractions/"]
COPY ["src/Company.Logging.Serilog.AspNetCore/Company.Logging.Serilog.AspNetCore.csproj", "Company.Logging.Serilog.AspNetCore/"]
COPY ["src/SampleMicroservice/SampleMicroservice.csproj", "SampleMicroservice/"]

RUN dotnet restore "SampleMicroservice/SampleMicroservice.csproj"

COPY src/ .
WORKDIR "/src/SampleMicroservice"
RUN dotnet build "SampleMicroservice.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SampleMicroservice.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SampleMicroservice.dll"]
