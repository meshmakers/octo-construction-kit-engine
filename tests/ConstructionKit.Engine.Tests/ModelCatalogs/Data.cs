namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

public class Data
{
    internal const string RootCatalog =
        "{\n  \"version\": \"1.0\",\n  \"updatedAt\": \"2025-10-09T18:19:07.612561Z\",\n  \"models\": [\n    {\n      \"modelName\": \"TestModel\",   \"catalogPath\": \"ck-models/v2/t/TestModel/catalog.json\"\n    }\n  ]\n}";

    internal const string TestModelModelCatalog1 =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersions\": [\n    {\n      \"majorVersion\": 1,\n   \"catalogPath\": \"ck-models/v2/t/TestModel/1/catalog.json\"\n    }\n  ],\n  \"updatedAt\": \"2025-10-09T18:16:23.181843Z\"\n}";
    internal const string TestModelModelCatalog2 =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersions\": [\n    {\n      \"majorVersion\": 1,\n   \"catalogPath\": \"ck-models/v2/t/TestModel/1/catalog.json\"\n    }\n,  {\n      \"majorVersion\": 2,\n   \"catalogPath\": \"ck-models/v2/t/TestModel/2/catalog.json\"\n    }\n  ],\n  \"updatedAt\": \"2025-10-09T18:16:23.181843Z\"\n}";
    internal const string TestModelModelCatalog3 =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersions\": [\n  " +
        "  {\n      \"majorVersion\": 1,\n   \"catalogPath\": \"ck-models/v2/t/TestModel/1/catalog.json\"\n    }\n," +
        "  {\n      \"majorVersion\": 2,\n   \"catalogPath\": \"ck-models/v2/t/TestModel/2/catalog.json\"\n    }\n," +
        "  {\n      \"majorVersion\": 3,\n   \"catalogPath\": \"ck-models/v2/t/TestModel/3/catalog.json\"\n    }\n" +
        "  ],\n  \"updatedAt\": \"2025-10-09T18:16:23.181843Z\"\n}";


    internal const string TestModelVersionsCatalog1 =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersion\": 1,\n  \"latestVersion\": \"1.0.0\",\n  \"description\": \"Test library\",\n  \"versions\": [\n    {\n      \"version\": \"1.0.0\",\n      \"fileName\": \"ck-test-model-1.0.0.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.0.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    }\n  ],\n  \"updatedAt\": \"2025-09-25T08:14:47.62282Z\"\n}";
    internal const string TestModelVersionsCatalog1Multiple =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersion\": 1,\n  \"latestVersion\": \"1.0.5\",\n  \"description\": \"Test library\",\n  \"versions\": [\n   " +
        " {\n      \"version\": \"1.0.0\",\n      \"fileName\": \"ck-test-model-1.0.0.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.0.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"1.0.1\",\n      \"fileName\": \"ck-test-model-1.0.1.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.1.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"1.0.2\",\n      \"fileName\": \"ck-test-model-1.0.2.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.2.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"1.0.3\",\n      \"fileName\": \"ck-test-model-1.0.3.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.3.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"1.0.4\",\n      \"fileName\": \"ck-test-model-1.0.4.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.4.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"1.0.5\",\n      \"fileName\": \"ck-test-model-1.0.5.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/1/ck-test-model-1.0.5.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    }\n " +
        " ],\n  \"updatedAt\": \"2025-09-25T08:14:47.62282Z\"\n}";
    internal const string TestModelVersionsCatalog2Multiple =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersion\": 2,\n  \"latestVersion\": \"2.1.2\",\n  \"description\": \"Test library\",\n  \"versions\": [\n   " +
        " {\n      \"version\": \"2.0.0\",\n      \"fileName\": \"ck-test-model-2.0.0.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/2/ck-test-model-2.0.0.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"2.0.1\",\n      \"fileName\": \"ck-test-model-2.0.1.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/2/ck-test-model-2.0.1.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"2.0.2\",\n      \"fileName\": \"ck-test-model-2.0.2.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/2/ck-test-model-2.0.2.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"2.0.3\",\n      \"fileName\": \"ck-test-model-2.0.3.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/2/ck-test-model-2.0.3.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"2.0.4\",\n      \"fileName\": \"ck-test-model-2.0.4.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/2/ck-test-model-2.0.4.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"2.1.2\",\n      \"fileName\": \"ck-test-model-2.1.2.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/2/ck-test-model-2.1.2.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    }\n " +
        " ],\n  \"updatedAt\": \"2025-09-25T08:14:47.62282Z\"\n}";
    internal const string TestModelVersionsCatalog3Multiple =
        "{\n  \"modelId\": \"TestModel\",\n  \"majorVersion\": 2,\n  \"latestVersion\": \"3.0.1\",\n  \"description\": \"Test library\",\n  \"versions\": [\n   " +
        " {\n      \"version\": \"3.0.0\",\n      \"fileName\": \"ck-test-model-3.0.0.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/3/ck-test-model-3.0.0.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    },\n " +
        " {\n      \"version\": \"3.0.1\",\n      \"fileName\": \"ck-test-model-3.0.1.json\",\n      \"filePath\": \"ck-models/v2/s/TestModel/3/ck-test-model-3.0.1.json\",\n      \"publishedAt\": \"2025-09-25T08:14:47.620984Z\"\n    }\n " +
        " ],\n  \"updatedAt\": \"2025-09-25T08:14:47.62282Z\"\n}";
}