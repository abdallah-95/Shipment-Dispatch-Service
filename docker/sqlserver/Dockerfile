# Official SQL Server 2022 image plus the full-text search component, which the
# official image does not ship. Used by docker-compose.yml and by the test fixture.
FROM mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04

# Must equal the engine version of the base tag above (2022-CU27 = 16.0.4295.3).
# When bumping the base tag, find the new version with:
#   SELECT SERVERPROPERTY('ProductVersion')
ARG MSSQL_FTS_VERSION=16.0.4295.3-1

USER root

# The base image trusts Microsoft's signing key but only lists the "prod" repo, which does
# not carry the engine packages, so add the SQL Server 2022 repo.
#
# The engine in this image is not registered with dpkg, so `apt-get install mssql-server-fts`
# would also install a second full copy of mssql-server (~2.7 GB more image). Download the
# package alone and install it with the mssql-server dependency check waived instead. The
# exact version pin guarantees the FTS component matches the engine it extends.
RUN echo "deb https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main" \
        > /etc/apt/sources.list.d/mssql-server-2022.list \
    && apt-get update \
    && cd /tmp \
    && apt-get download "mssql-server-fts=${MSSQL_FTS_VERSION}" \
    && dpkg --install --force-depends ./mssql-server-fts_*.deb \
    && rm -f ./mssql-server-fts_*.deb \
    && rm -rf /var/lib/apt/lists/*

USER mssql
