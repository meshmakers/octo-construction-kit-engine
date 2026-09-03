# Use a lightweight Nginx image
FROM nginx:alpine

# Create a directory for the JSON schemas
WORKDIR /usr/share/nginx/html

# Copy the JSON schema files into the container image
# Construction Kit schemas
COPY ./src/ConstructionKit.Contracts/Serialization/Schema/*.json /usr/share/nginx/html/
# Blueprint schemas
COPY ./src/ConstructionKit.Contracts/BlueprintCatalogs/Serialization/Schema/*.json /usr/share/nginx/html/
# Runtime schemas
COPY ./src/Runtime.Contracts/Serialization/Schema/*.json /usr/share/nginx/html/
COPY ./src/ConstructionKit.SchemaProvider/index.html /usr/share/nginx/html/

# Expose the HTTP port
EXPOSE 80
