#region

using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdows.crud.collections;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests.collections;

public class OrderedDictionaryExtensionsTests
{
    private class TestObject
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLogin { get; set; }
        public string ReadOnlyProperty => "readonly";
        public string WriteOnlyProperty { set { } }
        public string this[int index] => "indexer";
        public string ThrowsOnAccess => throw new InvalidOperationException("Property access failed");
    }

    [Fact]
    public void FromObject_ValidObject_CreatesCorrectDictionary()
    {
        var obj = new TestObject
        {
            Name = "John",
            Age = 30,
            IsActive = true,
            LastLogin = new DateTime(2023, 1, 1)
        };

        var dict = OrderedDictionaryExtensions.FromObject(obj);

        Assert.Equal("John", dict["Name"]);
        Assert.Equal(30, dict["Age"]);
        Assert.Equal(true, dict["IsActive"]);
        Assert.Equal(new DateTime(2023, 1, 1), dict["LastLogin"]);
        Assert.Equal("readonly", dict["ReadOnlyProperty"]);
        Assert.False(dict.ContainsKey("WriteOnlyProperty"));
        Assert.False(dict.ContainsKey("Item"));
        Assert.False(dict.ContainsKey("ThrowsOnAccess"));
    }

    [Fact]
    public void FromObject_ObjectWithNullValues_HandlesNullsCorrectly()
    {
        var obj = new TestObject
        {
            Name = "John",
            Age = 30,
            IsActive = false,
            LastLogin = null
        };

        var dict = OrderedDictionaryExtensions.FromObject(obj);

        Assert.Equal("John", dict["Name"]);
        Assert.Equal(30, dict["Age"]);
        Assert.Equal(false, dict["IsActive"]);
        Assert.Null(dict["LastLogin"]);
    }

    [Fact]
    public void FromObject_MaintainsPropertyOrder()
    {
        var obj = new TestObject
        {
            Name = "John",
            Age = 30,
            IsActive = true
        };

        var dict = OrderedDictionaryExtensions.FromObject(obj);
        var keys = dict.Keys.ToArray();

        Assert.Contains("Name", keys);
        Assert.Contains("Age", keys);
        Assert.Contains("IsActive", keys);
        Assert.Contains("LastLogin", keys);
        Assert.Contains("ReadOnlyProperty", keys);
    }

    [Fact]
    public void AddParameter_WithValue_AddsCorrectly()
    {
        var dict = new OrderedDictionary<string, object?>();

        dict.AddParameter("param1", "value1");
        dict.AddParameter("param2", 42);

        Assert.Equal("value1", dict["param1"]);
        Assert.Equal(42, dict["param2"]);
    }

    [Fact]
    public void AddParameter_WithNull_AddsDBNull()
    {
        var dict = new OrderedDictionary<string, object?>();

        dict.AddParameter("param1", null);

        Assert.Equal(DBNull.Value, dict["param1"]);
    }

    [Fact]
    public void AddParameter_OverwritesExisting()
    {
        var dict = new OrderedDictionary<string, object?>();
        dict.AddParameter("param1", "original");

        dict.AddParameter("param1", "updated");

        Assert.Equal("updated", dict["param1"]);
    }

    [Fact]
    public void TryAddParameter_NewKey_ReturnsTrueAndAdds()
    {
        var dict = new OrderedDictionary<string, object?>();

        var result = dict.TryAddParameter("param1", "value1");

        Assert.True(result);
        Assert.Equal("value1", dict["param1"]);
    }

    [Fact]
    public void TryAddParameter_ExistingKey_ReturnsFalseAndDoesNotModify()
    {
        var dict = new OrderedDictionary<string, object?>();
        dict.AddParameter("param1", "original");

        var result = dict.TryAddParameter("param1", "new");

        Assert.False(result);
        Assert.Equal("original", dict["param1"]);
    }

    [Fact]
    public void TryAddParameter_WithNull_AddsDBNull()
    {
        var dict = new OrderedDictionary<string, object?>();

        var result = dict.TryAddParameter("param1", null);

        Assert.True(result);
        Assert.Equal(DBNull.Value, dict["param1"]);
    }

    [Fact]
    public void RemoveParameter_ExistingKey_ReturnsTrueWithValue()
    {
        var dict = new OrderedDictionary<string, object?>();
        dict.AddParameter("param1", "value1");

        var result = dict.RemoveParameter("param1", out var value);

        Assert.True(result);
        Assert.Equal("value1", value);
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void RemoveParameter_NonExistentKey_ReturnsFalseWithNull()
    {
        var dict = new OrderedDictionary<string, object?>();

        var result = dict.RemoveParameter("nonexistent", out var value);

        Assert.False(result);
        Assert.Null(value);
    }

    [Fact]
    public void AddDbParameter_ValidParameter_AddsCorrectly()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var param = factory.CreateParameter();
        param.ParameterName = "param1";
        param.Value = "value1";

        dict.AddDbParameter(param);

        Assert.Equal(1, dict.Count);
        Assert.True(dict.ContainsKey("param1"));
        Assert.Equal(param, dict["param1"]);
    }

    [Fact]
    public void AddDbParameter_ParameterWithPrefix_TrimsPrefix()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer.ToString());

        var param1 = factory.CreateParameter();
        param1.ParameterName = "@param1";
        param1.Value = "value1";

        var param2 = factory.CreateParameter();
        param2.ParameterName = ":param2";
        param2.Value = "value2";

        var param3 = factory.CreateParameter();
        param3.ParameterName = "?param3";
        param3.Value = "value3";

        dict.AddDbParameter(param1);
        dict.AddDbParameter(param2);
        dict.AddDbParameter(param3);

        Assert.True(dict.ContainsKey("param1"));
        Assert.True(dict.ContainsKey("param2"));
        Assert.True(dict.ContainsKey("param3"));
        Assert.False(dict.ContainsKey("@param1"));
        Assert.False(dict.ContainsKey(":param2"));
        Assert.False(dict.ContainsKey("?param3"));
    }

    [Fact]
    public void AddDbParameter_NullParameter_DoesNotAdd()
    {
        var dict = new OrderedDictionary<string, DbParameter>();

        dict.AddDbParameter(null!);

        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void AddDbParameter_EmptyParameterName_ThrowsArgumentException()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var param = factory.CreateParameter();
        param.ParameterName = "";

        Assert.Throws<ArgumentException>(() => dict.AddDbParameter(param));
    }

    [Fact]
    public void AddDbParameter_NullParameterName_ThrowsArgumentException()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var param = factory.CreateParameter();
        param.ParameterName = null!;

        Assert.Throws<ArgumentException>(() => dict.AddDbParameter(param));
    }

    [Fact]
    public void AddDbParameter_OverwritesExisting()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());

        var param1 = factory.CreateParameter();
        param1.ParameterName = "param1";
        param1.Value = "original";

        var param2 = factory.CreateParameter();
        param2.ParameterName = "param1";
        param2.Value = "updated";

        dict.AddDbParameter(param1);
        dict.AddDbParameter(param2);

        Assert.Equal(1, dict.Count);
        Assert.Equal(param2, dict["param1"]);
        Assert.Equal("updated", dict["param1"].Value);
    }

    [Fact]
    public void GetParametersInOrder_ReturnsInInsertionOrder()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());

        var param1 = factory.CreateParameter();
        param1.ParameterName = "param1";
        param1.Value = "value1";

        var param2 = factory.CreateParameter();
        param2.ParameterName = "param2";
        param2.Value = "value2";

        var param3 = factory.CreateParameter();
        param3.ParameterName = "param3";
        param3.Value = "value3";

        dict.AddDbParameter(param1);
        dict.AddDbParameter(param2);
        dict.AddDbParameter(param3);

        var parameters = dict.GetParametersInOrder().ToArray();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(param1, parameters[0]);
        Assert.Equal(param2, parameters[1]);
        Assert.Equal(param3, parameters[2]);
    }

    [Fact]
    public void GetParametersInOrder_EmptyDictionary_ReturnsEmpty()
    {
        var dict = new OrderedDictionary<string, DbParameter>();

        var parameters = dict.GetParametersInOrder().ToArray();

        Assert.Empty(parameters);
    }

    [Fact]
    public void GetParametersInOrder_AfterRemoval_MaintainsOrder()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());

        var param1 = factory.CreateParameter();
        param1.ParameterName = "param1";

        var param2 = factory.CreateParameter();
        param2.ParameterName = "param2";

        var param3 = factory.CreateParameter();
        param3.ParameterName = "param3";

        dict.AddDbParameter(param1);
        dict.AddDbParameter(param2);
        dict.AddDbParameter(param3);

        dict.Remove("param2");

        var parameters = dict.GetParametersInOrder().ToArray();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(param1, parameters[0]);
        Assert.Equal(param3, parameters[1]);
    }

    [Fact]
    public void IntegrationTest_CompleteWorkflow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer.ToString());

        var testObj = new TestObject
        {
            Name = "Integration Test",
            Age = 25,
            IsActive = true,
            LastLogin = DateTime.Now
        };

        var objectDict = OrderedDictionaryExtensions.FromObject(testObj);
        var paramDict = new OrderedDictionary<string, DbParameter>();

        foreach (var kvp in objectDict)
        {
            var param = factory.CreateParameter();
            param.ParameterName = $"@{kvp.Key}";
            param.Value = kvp.Value ?? DBNull.Value;
            paramDict.AddDbParameter(param);
        }

        var parameters = paramDict.GetParametersInOrder().ToArray();

        Assert.True(parameters.Length >= 4);
        Assert.Contains(parameters, p => p.ParameterName == "@Name" && p.Value.Equals("Integration Test"));
        Assert.Contains(parameters, p => p.ParameterName == "@Age" && p.Value.Equals(25));
        Assert.Contains(parameters, p => p.ParameterName == "@IsActive" && p.Value.Equals(true));
    }

    [Fact]
    public void FromObject_AnonymousType_WorksCorrectly()
    {
        var obj = new { Name = "Anonymous", Count = 42, Active = true };

        var dict = OrderedDictionaryExtensions.FromObject(obj);

        Assert.Equal(3, dict.Count);
        Assert.Equal("Anonymous", dict["Name"]);
        Assert.Equal(42, dict["Count"]);
        Assert.Equal(true, dict["Active"]);
    }

    [Fact]
    public void FromObject_EmptyObject_ReturnsEmptyDictionary()
    {
        var obj = new { };

        var dict = OrderedDictionaryExtensions.FromObject(obj);

        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void ParameterPrefixHandling_AllCommonPrefixes_Handled()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());

        var testCases = new[]
        {
            ("@sqlserver", "sqlserver"),
            (":oracle", "oracle"),
            ("?odbc", "odbc"),
            ("noprefixparam", "noprefixparam"),
            ("@@@multipleprefix", "multipleprefix")
        };

        foreach (var (paramName, expectedKey) in testCases)
        {
            var param = factory.CreateParameter();
            param.ParameterName = paramName;
            param.Value = $"value_{expectedKey}";
            dict.AddDbParameter(param);
        }

        foreach (var (paramName, expectedKey) in testCases)
        {
            Assert.True(dict.ContainsKey(expectedKey), $"Expected key '{expectedKey}' not found");
            Assert.Equal($"value_{expectedKey}", dict[expectedKey].Value);
        }
    }

    [Fact]
    public void ObjectPropertyReflection_SkipsInvalidProperties()
    {
        var obj = new TestObject
        {
            Name = "Test",
            Age = 30
        };

        var dict = OrderedDictionaryExtensions.FromObject(obj);

        Assert.False(dict.ContainsKey("WriteOnlyProperty"));
        Assert.False(dict.ContainsKey("Item"));
        Assert.False(dict.ContainsKey("ThrowsOnAccess"));

        Assert.True(dict.ContainsKey("Name"));
        Assert.True(dict.ContainsKey("Age"));
        Assert.True(dict.ContainsKey("ReadOnlyProperty"));
    }

    [Fact]
    public void DbParameterHandling_WithDifferentTypes()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql.ToString());

        var stringParam = factory.CreateParameter();
        stringParam.ParameterName = "stringParam";
        stringParam.DbType = DbType.String;
        stringParam.Value = "test";

        var intParam = factory.CreateParameter();
        intParam.ParameterName = "intParam";
        intParam.DbType = DbType.Int32;
        intParam.Value = 42;

        var dateParam = factory.CreateParameter();
        dateParam.ParameterName = "dateParam";
        dateParam.DbType = DbType.DateTime;
        dateParam.Value = new DateTime(2023, 1, 1);

        dict.AddDbParameter(stringParam);
        dict.AddDbParameter(intParam);
        dict.AddDbParameter(dateParam);

        var parameters = dict.GetParametersInOrder().ToArray();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(stringParam, parameters[0]);
        Assert.Equal(intParam, parameters[1]);
        Assert.Equal(dateParam, parameters[2]);

        Assert.Equal("test", parameters[0].Value);
        Assert.Equal(42, parameters[1].Value);
        Assert.Equal(new DateTime(2023, 1, 1), parameters[2].Value);
    }

    [Fact]
    public void MixedParameterOperations_WorkCorrectly()
    {
        var dict = new OrderedDictionary<string, object?>();

        dict.AddParameter("first", "value1");
        dict.TryAddParameter("second", "value2");
        dict.TryAddParameter("first", "should_not_overwrite");
        dict.AddParameter("third", null);

        Assert.Equal(3, dict.Count);
        Assert.Equal("value1", dict["first"]);
        Assert.Equal("value2", dict["second"]);
        Assert.Equal(DBNull.Value, dict["third"]);
    }

    [Fact]
    public void RemoveParameter_WithDbParameterDictionary_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, DbParameter>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());

        var param = factory.CreateParameter();
        param.ParameterName = "testParam";
        param.Value = "testValue";

        dict.AddDbParameter(param);

        var result = dict.Remove("testParam", out var removedParam);

        Assert.True(result);
        Assert.Equal(param, removedParam);
        Assert.Equal(0, dict.Count);
    }
}
