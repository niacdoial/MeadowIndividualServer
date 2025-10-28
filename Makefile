

build:
	dotnet publish --self-contained --runtime linux-x64 -o bin -c Release

container: publish
	docker build . -t meadow-individual-server

.PHONY: build, container