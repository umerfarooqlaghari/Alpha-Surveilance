import importlib
import os
import sys
import unittest
from unittest.mock import MagicMock, patch

from sqlalchemy.exc import OperationalError


class InitDbRetryTests(unittest.TestCase):
    def test_init_db_retries_transient_connection_errors(self):
        with patch.dict(os.environ, {"DATABASE_URL": "postgresql://user:pass@localhost/db"}, clear=False):
            sys.modules.pop("app.database", None)
            database = importlib.import_module("app.database")

            connection = MagicMock()
            connection.execute.return_value = MagicMock()

            class DummyConnectionContext:
                def __init__(self, failures):
                    self.failures = list(failures)
                    self.calls = 0

                def __enter__(self):
                    if self.calls < len(self.failures):
                        error = self.failures[self.calls]
                        self.calls += 1
                        if error is not None:
                            raise error
                    return connection

                def __exit__(self, exc_type, exc, tb):
                    return False

            context = DummyConnectionContext([
                OperationalError("DB", None, Exception("temporary outage")),
                OperationalError("DB", None, Exception("temporary outage")),
                None,
            ])

            with patch.object(database.engine, "connect", return_value=context), patch.object(database.Base.metadata, "create_all") as create_all:
                database.init_db(max_retries=3, retry_delay_seconds=0)

            self.assertEqual(context.calls, 3)
            # init_db now also introspects the live embedding dimension,
            # queries the pgvector version, and creates the ANN index after
            # the extension is in place, so more than one statement runs.
            # The first statement must still be the extension creation.
            self.assertGreaterEqual(connection.execute.call_count, 1)
            first_stmt = str(connection.execute.call_args_list[0].args[0])
            self.assertIn("CREATE EXTENSION IF NOT EXISTS vector", first_stmt)
            executed = [str(c.args[0]) for c in connection.execute.call_args_list]
            self.assertTrue(any("CREATE INDEX IF NOT EXISTS" in s for s in executed))
            create_all.assert_called_once_with(bind=database.engine)


if __name__ == "__main__":
    unittest.main()
