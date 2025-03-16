# Nutze ein leichtgewichtiges Nginx-Image
FROM nginx:alpine

# Erstelle ein Verzeichnis für die JSON-Schemas
WORKDIR /usr/share/nginx/html

# Kopiere die JSON-Schema-Dateien ins Container-Image
COPY ./src/ConstructionKit.Contracts/Serialization/Schema/*.json /usr/share/nginx/html/
COPY ./src/ConstructionKit.SchemaProvider/index.html /usr/share/nginx/html/

# Exponiere den HTTP-Port
EXPOSE 80
