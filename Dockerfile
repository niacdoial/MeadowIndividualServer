FROM public.ecr.aws/amazonlinux/amazonlinux:2023 AS meadow-server-base

WORKDIR /usr/bin
COPY bin/Release/net8.0/linux-arm64/* .

COPY install-req-arm.sh .
RUN ["sh", "install-req-arm.sh"]

FROM meadow-server-base 
EXPOSE 8720/udp
ENTRYPOINT [ "/usr/bin/MeadowIndividualServer", "gamelift=true"]