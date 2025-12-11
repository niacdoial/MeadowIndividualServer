FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /bin
COPY bin/Release/net8/linux-x64/* .

EXPOSE 8720/udp
ENTRYPOINT [ "./MeadowIndividualServer" ]