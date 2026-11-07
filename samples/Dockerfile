# Either reference service, built from this source for the local stack
# (deploy/docker-compose.yaml). Build context is the repository root, because
# the samples reference src/ by ProjectReference and the root props and key.
#
#   docker build -f samples/Dockerfile --build-arg PROJECT=Screening.Api .
#
# 🔒 Demo only (ADR-0022). Building from source is a deviation from plan D4's
# "on preview packages", recorded in deploy/README.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "samples/$PROJECT/$PROJECT.csproj" -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
ENV PROJECT=$PROJECT
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$PROJECT.dll\""]
