# installs requirements for containers

# install libicu to prevent weird localization issues
yum install libicu -y

#
yum install libsodium -y
ln -s $(rpm -ql libsodium | grep libsodium.so | head -n 1) $(pwd)/libsodium.dll.so
