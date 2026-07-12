using Xunit;

// Several fixtures (SessionStore roundtrips, MainViewModel construction) share
// the session.json under the test host's AIO_Cache folder — run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
