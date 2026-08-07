using Xunit;

// Las arenas son estaticas por tipo cerrado: Arena<long> es UNA sola para todo
// el proceso. xUnit corre las clases de test en paralelo por defecto, asi que
// dos clases tocando Arena<long> al mismo tiempo se pisan los contadores y la
// lista libre.
//
// Sin esto los tests fallan de forma intermitente y parece un bug de la
// libreria. No lo es.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
