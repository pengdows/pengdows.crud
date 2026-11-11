#!/usr/bin/env python3
# Tests for gen_fidelity.resolve(): a declaration the generator writes must be one the server accepted (or a recorded
# decision), never an assumption the probe did not check.
#
# Run: python3 -m unittest tools/datatype-probe/test_gen_fidelity.py   (from the repository root)
import os
import sys
import unittest

_argv = sys.argv
sys.argv = [_argv[0], os.devnull]  # gen_fidelity reads its output directory at import
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_fidelity as g  # noqa: E402
sys.argv = _argv


class ResolveTests(unittest.TestCase):
    def test_a_widened_declaration_the_probe_never_accepted_is_not_written(self):
        # unsigned 32-bit widens to BIGINT; the probe accepted no BIGINT on this database, so there is no column to declare.
        accepted = {("InterBase", "signed 32-bit"): [("INTEGER", "INTEGER")]}
        self.assertIsNone(g.resolve(accepted, "InterBase", "unsigned 32-bit"))

    def test_a_widened_declaration_the_probe_accepted_is_used(self):
        # The same widening, where the probe accepted BIGINT for the signed 64-bit type on this database.
        accepted = {("PostgreSql", "signed 64-bit"): [("BIGINT", "bigint")]}
        self.assertEqual(g.resolve(accepted, "PostgreSql", "unsigned 32-bit"), ("BIGINT", "(widened)"))

    def test_a_widened_numeric_the_probe_refused_is_not_written(self):
        # unsigned 64-bit widens to NUMERIC(20,0), which InterBase refuses (its precision is at most 18).
        accepted = {("InterBase", "unsigned 64-bit"): []}
        self.assertIsNone(g.resolve(accepted, "InterBase", "unsigned 64-bit"))

    def test_interbase_timestamp_is_not_a_rowversion(self):
        # The probe accepts TIMESTAMP as a rowversion candidate; on InterBase that is a date-time, which cannot hold the 8 token bytes.
        accepted = {("InterBase", "rowversion / timestamp token"): [("TIMESTAMP", "TIMESTAMP")]}
        self.assertIsNone(g.resolve(accepted, "InterBase", "rowversion / timestamp token"))

    def test_hana_signed_8_bit_is_smallint_because_tinyint_is_unsigned(self):
        # HANA's TINYINT holds 0..255; the probe accepts it at create, but it cannot hold -5.
        accepted = {("SapHana", "signed 8-bit"): [("TINYINT", "TINYINT"), ("SMALLINT", "SMALLINT")]}
        self.assertEqual(g.resolve(accepted, "SapHana", "signed 8-bit"), ("SMALLINT", "SMALLINT"))


    def test_hana_uuid_is_declared_as_text_because_the_library_writes_guid_text(self):
        # HANA's GuidFormat is String: the library writes the GUID's text, so the column must hold text, not RAW(16).
        accepted = {("SapHana", "UUID / GUID"): [("RAW(16)", "varbinary"), ("CHAR(36)", "char")]}
        self.assertEqual(g.resolve(accepted, "SapHana", "UUID / GUID"), ("CHAR(36)", "char"))

    def test_interbase_uuid_is_declared_as_octets_because_the_library_writes_guid_bytes(self):
        # InterBase's GuidFormat is Binary: the library writes 16 bytes, so the column is CHAR(16) CHARACTER SET OCTETS.
        accepted = {("InterBase", "UUID / GUID"): [("CHAR(36)", "CHAR"), ("CHAR(16) CHARACTER SET OCTETS", "CHAR")]}
        self.assertEqual(g.resolve(accepted, "InterBase", "UUID / GUID"),
                         ("CHAR(16) CHARACTER SET OCTETS", "CHAR"))

    def test_interbase_unsigned_32_bit_uses_numeric_18_when_the_probe_accepts_it(self):
        # InterBase has no BIGINT; NUMERIC(18,0) holds every unsigned 32-bit value and the probe measures it.
        accepted = {("InterBase", "unsigned 32-bit"): [("NUMERIC(18,0)", "NUMERIC")]}
        self.assertEqual(g.resolve(accepted, "InterBase", "unsigned 32-bit"), ("NUMERIC(18,0)", "NUMERIC"))

    def test_hana_point_geometry_keeps_the_srid_of_the_sample(self):
        # The fidelity sample is SRID 4326; a column declared without an SRID is SRID 0 and refuses it.
        accepted = {("SapHana", "point geometry"): [("ST_POINT", "ST_POINT"), ("ST_GEOMETRY(4326)", "ST_GEOMETRY")]}
        self.assertEqual(g.resolve(accepted, "SapHana", "point geometry"), ("ST_GEOMETRY(4326)", "ST_GEOMETRY"))


    def test_a_required_declaration_the_probe_did_not_accept_is_refused_not_replaced(self):
        # InterBase's GUID bytes need the OCTETS column; a CHAR(36) column would make the library's binary write unreadable,
        # so without the OCTETS column there is no cell, rather than a text one.
        accepted = {("InterBase", "UUID / GUID"): [("CHAR(36)", "CHAR")]}
        self.assertIsNone(g.resolve(accepted, "InterBase", "UUID / GUID"))


    def test_unsigned_32_bit_keeps_the_library_bound_bigint_where_the_probe_accepts_it(self):
        # NUMERIC(18,0) also holds the value, but the committed declaration is the type the library binds (BIGINT).
        accepted = {("PostgreSql", "signed 64-bit"): [("BIGINT", "bigint")],
                    ("PostgreSql", "unsigned 32-bit"): [("NUMERIC(18,0)", "numeric")]}
        self.assertEqual(g.resolve(accepted, "PostgreSql", "unsigned 32-bit"), ("BIGINT", "(widened)"))

    def test_sql_server_rowversion_is_the_native_rowversion_not_binary_8(self):
        # ROWVERSION is the native type SQL Server and SQLite give a version column; BINARY(8) is only the fallback.
        accepted = {("SqlServer", "rowversion / timestamp token"): [("BINARY(8)", "binary"), ("ROWVERSION", "timestamp")]}
        self.assertEqual(g.resolve(accepted, "SqlServer", "rowversion / timestamp token"), ("ROWVERSION", "timestamp"))


    def test_hana_geography_keeps_the_srid_of_the_sample(self):
        # As the geometry types: the sample is SRID 4326, and a HANA column without an SRID is SRID 0.
        accepted = {("SapHana", "geography / geodetic"): [("ST_GEOMETRY", "ST_GEOMETRY"), ("ST_GEOMETRY(4326)", "ST_GEOMETRY")]}
        self.assertEqual(g.resolve(accepted, "SapHana", "geography / geodetic"), ("ST_GEOMETRY(4326)", "ST_GEOMETRY"))


    def test_flatfile_year_month_interval_is_refused_because_months_have_no_timespan(self):
        # pengdows.flatfile stores an INTERVAL as a TimeSpan; months have no fixed length, so no TimeSpan holds the value.
        accepted = {("FlatFile", "year-month interval"): [("INTERVAL", "INTERVAL")]}
        self.assertIsNone(g.resolve(accepted, "FlatFile", "year-month interval"))

    def test_flatfile_generic_interval_is_refused_because_months_have_no_timespan(self):
        accepted = {("FlatFile", "generic interval"): [("INTERVAL", "INTERVAL")]}
        self.assertIsNone(g.resolve(accepted, "FlatFile", "generic interval"))

    def test_flatfile_day_time_interval_is_kept(self):
        accepted = {("FlatFile", "day-time interval"): [("INTERVAL", "INTERVAL")]}
        self.assertEqual(g.resolve(accepted, "FlatFile", "day-time interval"), ("INTERVAL", "INTERVAL"))


    def test_interbase_second_precision_keeps_the_time_of_day(self):
        # DATE drops the time; the probe accepted TIMESTAMP on InterBase, which keeps it.
        accepted = {("InterBase", "second-precision datetime"): [("DATE", "DATE"), ("TIMESTAMP", "TIMESTAMP")]}
        self.assertEqual(g.resolve(accepted, "InterBase", "second-precision datetime"), ("TIMESTAMP", "TIMESTAMP"))

    def test_interbase_national_text_is_a_varchar_the_pinned_utf8_client_round_trips(self):
        # NCHAR is Latin-1 and fails for other text; a VARCHAR on the default NONE charset round-trips any text, because the
        # driver's NONE is pinned to UTF-8 (docs/advanced-types.md). No connection charset change is needed.
        accepted = {("InterBase", "national / Unicode character"): [
            ("NCHAR(40)", "CHAR"), ("VARCHAR(100)", "VARCHAR")]}
        self.assertEqual(g.resolve(accepted, "InterBase", "national / Unicode character"), ("VARCHAR(100)", "VARCHAR"))

    def test_interbase_national_text_is_refused_when_no_utf8_column_was_probed(self):
        accepted = {("InterBase", "national / Unicode character"): [("NCHAR(40)", "CHAR")]}
        self.assertIsNone(g.resolve(accepted, "InterBase", "national / Unicode character"))


    def test_informix_geography_is_refused_when_the_integration_database_lacks_the_spatial_type(self):
        # The probe accepted ST_GEOMETRY, but the integration database reports Type (st_geometry) not found: no column there.
        accepted = {("Informix", "geography / geodetic"): [("ST_GEOMETRY", "ST_GEOMETRY")]}
        self.assertIsNone(g.resolve(accepted, "Informix", "geography / geodetic"))


    def test_flatfile_year_month_is_the_year_to_month_column_once_probed(self):
        # pengdows.flatfile now has INTERVAL YEAR TO MONTH (text, ISO 8601): the probe accepts it, and the cell is that column.
        accepted = {("FlatFile", "year-month interval"): [("INTERVAL", "INTERVAL"),
                                                         ("INTERVAL YEAR TO MONTH", "INTERVAL YEAR TO MONTH")]}
        self.assertEqual(g.resolve(accepted, "FlatFile", "year-month interval"),
                         ("INTERVAL YEAR TO MONTH", "INTERVAL YEAR TO MONTH"))


    def test_native_point_is_refused_on_mysql_where_point_is_geometry(self):
        # native point is PostgreSQL's point (CLR PostgreSqlPoint); MySQL's POINT is a geometry, covered by point geometry.
        accepted = {("MySql", "native point"): [("POINT", "POINT")]}
        self.assertIsNone(g.resolve(accepted, "MySql", "native point"))

    def test_native_point_stays_declared_on_postgresql(self):
        accepted = {("PostgreSql", "native point"): [("POINT", "point")]}
        self.assertEqual(g.resolve(accepted, "PostgreSql", "native point"), ("POINT", "point"))

    def test_sqlite_transaction_id_is_bigint_not_an_accepted_unknown_name(self):
        # SQLite accepts any type name, so XID means nothing there; the 32-bit value is stored as BIGINT.
        accepted = {("Sqlite", "32-bit transaction ID"): [("XID", "XID"), ("BIGINT", "INTEGER")]}
        self.assertEqual(g.resolve(accepted, "Sqlite", "32-bit transaction ID"), ("BIGINT", "INTEGER"))

    def test_32_bit_transaction_id_is_bigint_where_the_probe_accepts_it(self):
        accepted = {("SqlServer", "32-bit transaction ID"): [("BIGINT", "bigint"), ("NUMERIC(10,0)", "numeric")]}
        self.assertEqual(g.resolve(accepted, "SqlServer", "32-bit transaction ID"), ("BIGINT", "bigint"))


    def test_aurora_mysql_uses_the_mysql_bit_width(self):
        # Aurora MySQL runs MySQL's engine: its bit string is MySQL's fixed BIT(3), not the probe's first accepted BIT(8).
        accepted = {("AuroraMySql", "bit / bitstring"): [("BIT(8)", "bit(8)")]}
        self.assertEqual(g.resolve(accepted, "AuroraMySql", "bit / bitstring"), ("BIT(3)", "bit(3)"))

    def test_aurora_postgresql_rowversion_is_bytea_as_postgresql_is(self):
        accepted = {("AuroraPostgreSql", "rowversion / timestamp token"): [("TIMESTAMP", "timestamp")],
                    ("AuroraPostgreSql", "varying binary"): [("BYTEA", "bytea")]}
        self.assertEqual(g.resolve(accepted, "AuroraPostgreSql", "rowversion / timestamp token"), ("BYTEA", "bytea"))

    def test_postgresql_national_text_stays_varchar_when_nchar_is_accepted(self):
        accepted = {("PostgreSql", "national / Unicode character"): [("NCHAR(40)", "bpchar"), ("VARCHAR(100)", "varchar")]}
        self.assertEqual(g.resolve(accepted, "PostgreSql", "national / Unicode character")[0], "VARCHAR(100)")

    def test_aurora_postgresql_transaction_id_is_xid8_as_postgresql_is(self):
        accepted = {("AuroraPostgreSql", "transaction ID"): [("XID", "xid"), ("XID8", "xid8")]}
        self.assertEqual(g.resolve(accepted, "AuroraPostgreSql", "transaction ID"), ("XID8", "xid8"))


    def test_prerequisite_ddl_is_emitted_only_for_the_declaration_it_creates(self):
        # The enum's DDL must run before the cell, and only when the cell declares the enum it creates.
        prereqs = {"PostgreSql": {"enumeration": {"decl": "probe_enum", "ddl": ["CREATE TYPE probe_enum AS ENUM ('a','b')"]}}}
        resolved_used = {"PostgreSql": {"enumeration": ("probe_enum", "public.probe_enum")}}
        resolved_other = {"PostgreSql": {"enumeration": ("ENUM('a','b')", "ENUM")}}
        self.assertEqual(g.prerequisites(prereqs, resolved_used),
                         {"PostgreSql": {"enumeration": ["CREATE TYPE probe_enum AS ENUM ('a','b')"]}})
        self.assertEqual(g.prerequisites(prereqs, resolved_other), {})


    def test_prerequisite_matches_the_probes_case_of_the_declaration(self):
        # The server reports a probed extension type in upper case (LTREE); the prerequisite names it in lower case.
        prereqs = {"PostgreSql": {"hierarchical path": {"decl": "ltree", "ddl": ["CREATE EXTENSION IF NOT EXISTS ltree"]}}}
        resolved = {"PostgreSql": {"hierarchical path": ("LTREE", "ltree")}}
        self.assertEqual(g.prerequisites(prereqs, resolved),
                         {"PostgreSql": {"hierarchical path": ["CREATE EXTENSION IF NOT EXISTS ltree"]}})


    def test_postgresql_map_is_the_hstore_value_type_the_library_documents(self):
        # docs/advanced-types.md: PostgreSQL hstore is the HStore value type. The native row for it uses HStore, not a dictionary.
        clr, val = g.native_clr("PostgreSql", "map / dictionary", "Dictionary<string,object>", "new Dictionary<string, object>()")
        self.assertEqual(clr, "HStore")
        self.assertIn("HStore.Parse", val)
        self.assertEqual(g.native_clr("DuckDB", "map / dictionary", "Dictionary<string,object>", "V")[0], "Dictionary<string,object>")


    def test_postgresql_hierarchical_path_is_a_native_ltree_column(self):
        # The ltree column needs its native type on the cell, so the library casts the bound text to ltree (PostgreSQL refuses
        # an implicit text-to-ltree conversion).
        self.assertEqual(g.NATIVE_TYPE[("PostgreSql", "hierarchical path")], "ltree")
        self.assertEqual(g.NATIVE_TYPE[("AuroraPostgreSql", "hierarchical path")], "ltree")


    def test_postgresql_ltree_is_text_in_its_dotted_form_for_aurora_too(self):
        # ltree labels are dot-separated (1.2), not HierarchyId's /1/2/; the native value is that text, on Aurora as on PostgreSQL.
        self.assertEqual(g.native_clr("PostgreSql", "hierarchical path", "HierarchyId", "x"), ("string", '"1.2"'))
        self.assertEqual(g.native_clr("AuroraPostgreSql", "hierarchical path", "HierarchyId", "x"), ("string", '"1.2"'))


    def test_duckdb_time_with_zone_is_declared_not_refused(self):
        # The server accepts TIMETZ and reads it back as a DateTimeOffset (results/DuckDB.jsonl), so no refusal stands.
        self.assertNotIn(("DuckDB", "time with zone"), g.DB_REFUSED)


    def test_informix_time_is_the_fractional_second_form_the_dialect_maps(self):
        # InformixDialect maps DbType.Time to DATETIME HOUR TO FRACTION(5); the HOUR TO SECOND form drops fractions.
        self.assertEqual(g.DB_REQUIRED[("Informix", "time without zone")], "DATETIME HOUR TO FRACTION(5)")


    def test_firebird_has_no_variable_length_array_so_sql_array_is_refused_with_the_reason(self):
        # Firebird's array dimensions are fixed (fblangref 4.0, array_dim): a column of INTEGER[10] reads back as ten elements,
        # so a variable-length SQL array is refused. The fixed-length array semantic is the Firebird array, and it is declared.
        self.assertIn(("Firebird", "SQL array"), g.DB_REFUSED)
        self.assertIn("fixed", g.DB_REFUSED[("Firebird", "SQL array")])
        self.assertNotIn(("Firebird", "fixed-length array"), g.DB_REFUSED)


    def test_db2_array_type_is_refused_with_the_server_reason(self):
        # Db2 refuses an ARRAY type in a column definition (SQL20441N, results/Db2.jsonl): the refusal records that reason.
        self.assertIn(("Db2", "SQL array"), g.DB_REFUSED)
        self.assertIn("SQL20441N", g.DB_REFUSED[("Db2", "SQL array")])


    def test_informix_raw_bson_bytes_are_refused_and_the_json_text_row_carries_bson(self):
        # Raw bytes have no cast to JSON on Informix (No cast from byte to json), so the generic BSON cell is refused. Informix's
        # BSON is written from JSON text (?::JSON::BSON) by the native binary JSON row, which is declared.
        self.assertIn(("Informix", "BSON"), g.DB_REFUSED)
        self.assertIn("native binary JSON", g.DB_REFUSED[("Informix", "BSON")])
        self.assertNotIn(("Informix", "BSON"), g.NATIVE_CLR)


    def test_interbase_arrays_are_fixed_dimension_like_firebird_so_sql_array_is_refused(self):
        # InterBase accepts INTEGER[10] (results/InterBase.jsonl), the same fixed-dimension array as Firebird: a variable-length
        # SQL array is not what the server has, so the refusal records that.
        self.assertIn(("InterBase", "SQL array"), g.DB_REFUSED)
        self.assertIn("fixed", g.DB_REFUSED[("InterBase", "SQL array")])


    def test_postgresql_extension_types_are_refused_as_extension_gaps_with_live_evidence(self):
        # pgvector and PostGIS are extensions: the default testbed image has neither, and the extension images accept the types
        # (results/versions/PostgreSql-pgvector.jsonl, results/versions/PostgreSql-postgis.jsonl).
        self.assertIn("extension", g.DB_REFUSED[("PostgreSql", "vector embedding")])
        self.assertIn("pgvector", g.DB_REFUSED[("PostgreSql", "vector embedding")])
        self.assertIn("extension", g.DB_REFUSED[("PostgreSql", "geography / geodetic")])
        self.assertIn("PostGIS", g.DB_REFUSED[("PostgreSql", "geography / geodetic")])


    def test_oracle_varray_is_declared_as_its_native_collection_type_not_refused(self):
        # The library now builds a declared VARRAY from its elements' JSON (OracleDialect.CollectionArgument), so the SQL array
        # row carries the VARRAY's native type and is no longer a binding gap.
        self.assertEqual(g.NATIVE_TYPE[("Oracle", "SQL array")], "probe_varray")
        self.assertNotIn(("Oracle", "SQL array"), g.DB_REFUSED)


    def test_aurora_postgresql_extensions_are_declared_from_its_own_probe_while_postgresql_stays_refused(self):
        # Aurora PostgreSQL installs pgvector and PostGIS (AWS extension list); its own probe accepted both
        # (results/AuroraPostgreSql.jsonl), so the engine's extension refusal does not apply to Aurora.
        probed = {("AuroraPostgreSql", "vector embedding"): [("vector(3)", "vector")]}
        accepted = {("AuroraPostgreSql", "vector embedding"): [("vector(3)", "float[]")]}
        self.assertIsNotNone(g.resolve(accepted, "AuroraPostgreSql", "vector embedding"))
        self.assertIsNone(g.resolve({("PostgreSql", "vector embedding"): [("vector(3)", "vector")]}, "PostgreSql", "vector embedding"))
        self.assertIn(("AuroraPostgreSql", "vector embedding"), g.OWN_EXTENSIONS)
        self.assertIn(("AuroraPostgreSql", "geography / geodetic"), g.OWN_EXTENSIONS)


    def test_a_schema_qualified_provider_type_is_declared_unqualified(self):
        # The Aurora testbed's per-run schema (aurora_pg_test_<ms>) qualifies an extension type; the declaration keeps the type's name.
        accepted = {("AuroraPostgreSql", "vector embedding"): [("vector(3)", "aurora_pg_test_1794239104498.vector")]}
        self.assertEqual(g.resolve(accepted, "AuroraPostgreSql", "vector embedding"), ("vector(3)", "vector"))


    def test_saphana_vector_is_refused_with_the_images_it_was_checked_on(self):
        # REAL_VECTOR is refused on saphana/hanaexpress 2.00.088 (latest) and 2.00.082, and the catalogue has no vector type
        # (results/versions/SapHana-2.00.088-latest.jsonl; a scratch catalogue query on both builds).
        reason = g.DB_REFUSED[("SapHana", "vector embedding")]
        self.assertIn("2.00.088", reason)
        self.assertIn("2.00.082", reason)
        self.assertIn("REAL_VECTOR", reason)


    def test_mysql_and_mariadb_vector_are_declared_as_their_native_vector_now_that_the_defaults_are_newer(self):
        # The default images are MySQL 9.7 and MariaDB 11.8 (TestbedImageMatrix), which have VECTOR; the library writes and reads
        # the native VECTOR(3) through its conversion functions (MySqlDialect, MariaDbDialect).
        self.assertEqual(g.NATIVE_TYPE[("MySql", "vector embedding")], "VECTOR(3)")
        self.assertEqual(g.NATIVE_TYPE[("MariaDb", "vector embedding")], "VECTOR(3)")
        self.assertNotIn(("MySql", "vector embedding"), g.DB_REFUSED)
        self.assertNotIn(("MariaDb", "vector embedding"), g.DB_REFUSED)


    def test_yugabytedb_xml_is_refused_because_its_values_are_refused_not_its_column(self):
        # YugabyteDB accepts an XML column declaration, but an INSERT of an XML value fails with "unsupported XML feature"
        # (run 2026-11-10, YugabyteDb suite). The refusal says that: the declaration alone is not support.
        self.assertIn(("YugabyteDb", "XML"), g.DB_REFUSED)
        self.assertIn("unsupported XML feature", g.DB_REFUSED[("YugabyteDb", "XML")])


    def test_oracle_rowid_refusal_says_it_is_read_as_text_and_never_written(self):
        # A ROWID is read as its text (ROWIDTOCHAR, live-checked on Oracle 23ai) and is NonInsertable/NonUpdateable: the
        # fidelity cell writes a value, so it stays refused, for that reason only.
        reason = g.DB_REFUSED[("Oracle", "row identifier / ROWID")]
        self.assertIn("ROWIDTOCHAR", reason)
        self.assertIn("NonInsertable", reason)
        self.assertNotIn("reads back NULL", reason)


    def test_sybase_ase_spatial_refusals_cite_the_probe_not_an_unverified_vendor_claim(self):
        # ASE 16 refuses POINT, LINESTRING and POLYGON ("Can't find type", results/SybaseASE.jsonl). SAP's own ASE spatial
        # documentation was not found, so the reason names the probe, not a documentation source.
        for sem in ("point geometry", "line / linestring geometry", "polygon geometry"):
            if ("SybaseASE", sem) in g.DB_REFUSED:
                self.assertIn("probe", g.DB_REFUSED[("SybaseASE", sem)])
                self.assertIn("no ASE spatial documentation", g.DB_REFUSED[("SybaseASE", sem)])


    def test_snowflake_is_enumerated_and_its_accepted_types_are_declared(self):
        # Snowflake is in the integration run (cloud, credentials set), and its probe accepted 96 declarations
        # (results/Snowflake.jsonl), so the generator declares them rather than reporting every cell as unrecorded.
        self.assertIn("Snowflake", g.DB_ENUM)
        accepted = {("Snowflake", "signed 8-bit"): [("NUMBER(3,0)", "NUMBER")]}
        self.assertIsNotNone(g.resolve(accepted, "Snowflake", "signed 8-bit"))


    def test_snowflake_array_columns_are_native_arrays_not_vectors(self):
        # A Snowflake ARRAY column takes its array as JSON (PARSE_JSON) and is read as TO_JSON (SnowflakeArrayTests); the
        # fidelity cells for SQL array and list declare that native type, so they are not written as VECTOR casts.
        self.assertEqual(g.NATIVE_TYPE[("Snowflake", "SQL array")], "ARRAY")
        self.assertEqual(g.NATIVE_TYPE[("Snowflake", "list / multiset / set collection")], "ARRAY")


    def test_snowflake_map_and_variant_are_native_semi_structured_columns(self):
        # A Snowflake OBJECT takes its dictionary as JSON and a VARIANT takes its value as JSON (SnowflakeSemiStructuredTests).
        self.assertEqual(g.NATIVE_TYPE[("Snowflake", "map / dictionary")], "OBJECT")
        self.assertEqual(g.NATIVE_TYPE[("Snowflake", "variant / dynamic value")], "VARIANT")


    def test_snowflake_external_file_is_refused_until_a_stage_fixture_exists(self):
        # A Snowflake FILE value names a file on a stage; the fidelity test has no stage, so the cell is refused with that reason.
        self.assertIn(("Snowflake", "external-file reference"), g.DB_REFUSED)
        self.assertIn("stage", g.DB_REFUSED[("Snowflake", "external-file reference")])


if __name__ == "__main__":
    unittest.main()
