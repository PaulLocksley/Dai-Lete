FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

RUN apt-get -y update && apt-get -y upgrade && apt-get install -y ffmpeg
ENV baseAddress=pod.locksley.dev
ENV accessToken=12345
ENV proxyAddress=prx.locksley.dev


FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
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
EXPOSE 4011
ENTRYPOINT ["dotnet", "Dai-Lete.dll"]
