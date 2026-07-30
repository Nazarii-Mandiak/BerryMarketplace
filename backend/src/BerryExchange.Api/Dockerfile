FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/BerryExchange.Api/BerryExchange.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# libSkiaSharp.so links against fontconfig even for non-text operations (photo
# resize/re-encode) - without it, SkiaSharp throws DllNotFoundException at first use.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BerryExchange.Api.dll"]
