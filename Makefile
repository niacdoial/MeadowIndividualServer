

build:
	dotnet publish --self-contained --runtime linux-arm64 -o bin -c Release

container: build
	docker build . -t meadow-individual-server

.PHONY: build, container