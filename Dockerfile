FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
RUN apt-get -y update && apt-get -y upgrade && apt-get install -y ffmpeg
ARG baseAddress=127.0.0.1
ARG accessToken=12345



FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Dai-Lete/Dai-Lete.csproj", "Dai-Lete/"]
RUN dotnet restore "Dai-Lete/Dai-Lete.csproj"
COPY . .
WORKDIR "/src/Dai-Lete"
RUN dotnet build "Dai-Lete.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Dai-Lete.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Dai-Lete.dll"]
