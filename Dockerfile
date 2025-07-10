FROM mcr.microsoft.com/dotnet/sdk:9.0.302-alpine3.21@sha256:60bd1997ac6a5d3c838a45256fbf1d7da538f2508eabdccc81f24682be968972 AS build

COPY src/Storage ./Storage
COPY src/DbTools ./DbTools
COPY src/Storage/Migration ./Migration

WORKDIR DbTools/
RUN dotnet build ./DbTools.csproj -c Release -o /app_tools

# Comment in the following line for local development
# RUN mkdir -p /DbTools/bin/Debug/net9.0 && cp /app_tools/DbTools /DbTools/bin/Debug/net9.0/DbTools

WORKDIR ../Storage/

RUN dotnet build ./Altinn.Platform.Storage.csproj -c Release -o /app_output
RUN dotnet publish ./Altinn.Platform.Storage.csproj -c Release -o /app_output

FROM mcr.microsoft.com/dotnet/aspnet:9.0.7-alpine3.22@sha256:53867b9ebb86beab644de47226867d255d0360e38324d2afb3ff4d2f2933e33f AS final
EXPOSE 5010
WORKDIR /app
COPY --from=build /app_output .
COPY --from=build /Storage/Migration ./Migration

# Add support for not only English cultures
RUN apk add --no-cache icu-libs icu-data-full tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

ENTRYPOINT ["dotnet", "Altinn.Platform.Storage.dll"]
