# pengdows.crud.fakeDb

`pengdows.crud.fakeDb` provides a fake ADO.NET provider that you can use to **mock low-level database calls**. It lets `pengdows.crud` execute SQL without a real database connection, which is handy for integration or unit tests. The package ships with schema files to emulate different products so tests remain provider agnostic.
