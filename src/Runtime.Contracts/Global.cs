using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Meshmakers.Octo.Runtime.Engine")]

// AB#5528: RtSecretValue.RawValue (the plaintext of pending/legacy values) is internal. The BSON
// serializer writes legacy values back unchanged (AB#5533) and the tests verify the state model.
[assembly: InternalsVisibleTo("Meshmakers.Octo.Runtime.Engine.MongoDb")]
[assembly: InternalsVisibleTo("Meshmakers.Octo.Runtime.Engine.Tests")]
