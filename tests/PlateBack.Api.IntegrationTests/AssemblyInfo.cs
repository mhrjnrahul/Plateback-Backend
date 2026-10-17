using PlateBack.Api.IntegrationTests.Infrastructure;

// One API host and one test database for the whole run. Tests run one at a
// time (xunit.runner.json) because each one resets the shared demo data.
[assembly: AssemblyFixture(typeof(PlateBackFactory))]
