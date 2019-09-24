using Chr.Avro.Resolution;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Chr.Avro.Abstract
{
    /// <summary>
    /// Builds Avro schemas for .NET types.
    /// </summary>
    public interface ISchemaBuilder
    {
        /// <summary>
        /// Builds a schema.
        /// </summary>
        /// <typeparam name="T">
        /// The type to build a schema for.
        /// </typeparam>
        /// <param name="cache">
        /// An optional schema cache. The cache can be used to provide schemas for certain types,
        /// and it will also be populated as the schema is built.
        /// </param>
        /// <returns>
        /// A schema that matches the type.
        /// </returns>
        Schema BuildSchema<T>(IDictionary<Type, Schema> cache = null);

        /// <summary>
        /// Builds a schema.
        /// </summary>
        /// <param name="type">
        /// The type to build a schema for.
        /// </param>
        /// <param name="cache">
        /// An optional schema cache. The cache can be used to provide schemas for certain types,
        /// and it will also be populated as the schema is built.
        /// </param>
        /// <returns>
        /// A schema that matches the type.
        /// </returns>
        Schema BuildSchema(Type type, IDictionary<Type, Schema> cache = null);
    }

    /// <summary>
    /// Builds Avro schemas for specific type resolutions. Used by <see cref="SchemaBuilder" /> to
    /// break apart schema building logic.
    /// </summary>
    public interface ISchemaBuilderCase
    {
        /// <summary>
        /// Builds a schema for a type resolution. If the case does not apply to the provided
        /// resolution, this method should throw <see cref="UnsupportedTypeException" />.
        /// </summary>
        /// <param name="resolution">
        /// The resolution to build a schema for.
        /// </param>
        /// <param name="cache">
        /// A schema cache. If a schema is cached for a specific type, that schema will be returned
        /// for all subsequent occurrences of the type.
        /// </param>
        /// <returns>
        /// A subclass of <see cref="Schema" />.
        /// </returns>
        Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache);
    }

    /// <summary>
    /// A schema builder configured with a reasonable set of default cases.
    /// </summary>
    public class SchemaBuilder : ISchemaBuilder
    {
        /// <summary>
        /// A list of cases that the schema builder will attempt to apply. If the first case does
        /// not match, the schema builder will try the next case, and so on until all cases have
        /// been tested.
        /// </summary>
        public IEnumerable<ISchemaBuilderCase> Cases { get; }

        /// <summary>
        /// A resolver to retrieve type information from.
        /// </summary>
        public ITypeResolver Resolver { get; }

        /// <summary>
        /// Creates a new schema builder.
        /// </summary>
        /// <param name="typeResolver">
        /// A resolver to retrieve type information from. If no resolver is provided, the schema
        /// builder will use the default <see cref="DataContractResolver" />.
        /// </param>
        public SchemaBuilder(ITypeResolver typeResolver = null) : this(CreateCaseBuilders(), typeResolver) { }

        /// <summary>
        /// Creates a new schema builder.
        /// </summary>
        /// <param name="caseBuilders">
        /// A list of case builders.
        /// </param>
        /// <param name="typeResolver">
        /// A resolver to retrieve type information from. If no resolver is provided, the schema
        /// builder will use the default <see cref="DataContractResolver" />.
        /// </param>
        public SchemaBuilder(IEnumerable<Func<ISchemaBuilder, ISchemaBuilderCase>> caseBuilders, ITypeResolver typeResolver = null)
        {
            var cases = new List<ISchemaBuilderCase>();

            Cases = cases;
            Resolver = typeResolver ?? new DataContractResolver();

            // initialize cases last so that the schema builder is fully ready:
            foreach (var builder in caseBuilders ?? CreateCaseBuilders())
            {
                cases.Add(builder(this));
            }
        }

        /// <summary>
        /// Builds a schema.
        /// </summary>
        /// <typeparam name="T">
        /// The type to build a schema for.
        /// </typeparam>
        /// <param name="cache">
        /// An optional schema cache. The cache can be used to provide schemas for certain types,
        /// and it will also be populated as the schema is built.
        /// </param>
        /// <returns>
        /// A schema that matches the type.
        /// </returns>
        /// <exception cref="AggregateException">
        /// Thrown when no case matches the type. <see cref="AggregateException.InnerExceptions" />
        /// will be contain the exceptions thrown by each case.
        /// </exception>
        public Schema BuildSchema<T>(IDictionary<Type, Schema> cache = null)
        {
            return BuildSchema(typeof(T), cache);
        }

        /// <summary>
        /// Builds a schema.
        /// </summary>
        /// <param name="type">
        /// The type to build a schema for.
        /// </param>
        /// <param name="cache">
        /// An optional schema cache. The cache can be used to provide schemas for certain types,
        /// and it will also be populated as the schema is built.
        /// </param>
        /// <returns>
        /// A schema that matches the type.
        /// </returns>
        /// <exception cref="AggregateException">
        /// Thrown when no case matches the type. <see cref="AggregateException.InnerExceptions" />
        /// will be contain the exceptions thrown by each case.
        /// </exception>
        public Schema BuildSchema(Type type, IDictionary<Type, Schema> cache = null)
        {
            if (cache == null)
            {
                cache = new ConcurrentDictionary<Type, Schema>();
            }

            var resolution = Resolver.ResolveType(type);

            if (!cache.TryGetValue(resolution.Type, out var schema))
            {
                var exceptions = new List<Exception>();

                foreach (var @case in Cases)
                {
                    try
                    {
                        schema = @case.BuildSchema(resolution, cache);
                        break;
                    }
                    catch (UnsupportedTypeException exception)
                    {
                        exceptions.Add(exception);
                    }
                }

                if (schema == null)
                {
                    throw new AggregateException($"No schema builder case could be applied to {resolution.Type.FullName} ({resolution.GetType().Name}).");
                }
            }

            if (resolution.IsNullable)
            {
                return new UnionSchema(new Schema[] { new NullSchema(), schema });
            }

            return schema;
        }

        /// <summary>
        /// Creates a default list of case builders.
        /// </summary>
        public static IEnumerable<Func<ISchemaBuilder, ISchemaBuilderCase>> CreateCaseBuilders()
        {
            return new Func<ISchemaBuilder, ISchemaBuilderCase>[]
            {
                builder => new ArraySchemaBuilderCase(builder),
                builder => new BooleanSchemaBuilderCase(),
                builder => new BytesSchemaBuilderCase(),
                builder => new DecimalSchemaBuilderCase(),
                builder => new DoubleSchemaBuilderCase(),
                builder => new DurationSchemaBuilderCase(),
                builder => new EnumSchemaBuilderCase(),
                builder => new FloatSchemaBuilderCase(),
                builder => new IntSchemaBuilderCase(),
                builder => new LongSchemaBuilderCase(),
                builder => new MapSchemaBuilderCase(builder),
                builder => new RecordSchemaBuilderCase(builder),
                builder => new StringSchemaBuilderCase(),
                builder => new TimestampSchemaBuilderCase(),
                builder => new UriSchemaBuilderCase(),
                builder => new UuidSchemaBuilderCase()
            };
        }
    }

    /// <summary>
    /// A base <see cref="ISchemaBuilderCase" /> implementation.
    /// </summary>
    public abstract class SchemaBuilderCase : ISchemaBuilderCase
    {
        /// <summary>
        /// Builds a schema for a type resolution. If the case does not apply to the provided
        /// resolution, this method should throw <see cref="UnsupportedTypeException" />.
        /// </summary>
        /// <param name="resolution">
        /// The resolution to build a schema for.
        /// </param>
        /// <param name="cache">
        /// A schema cache. If a schema is cached for a specific type, that schema will be returned
        /// for all subsequent occurrences of the type.
        /// </param>
        /// <returns>
        /// A subclass of <see cref="Schema" />.
        /// </returns>
        public abstract Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache);
    }

    /// <summary>
    /// A schema builder case that matches <see cref="ArrayResolution" />.
    /// </summary>
    public class ArraySchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// A schema builder instance that will be used to resolve array item types.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the schema builder is set to null.
        /// </exception>
        public ISchemaBuilder SchemaBuilder { get; }

        /// <summary>
        /// Creates a new array schema builder case.
        /// </summary>
        /// <param name="schemaBuilder">
        /// A schema builder instance that will be used to resolve array item types.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the schema builder is null.
        /// </exception>
        public ArraySchemaBuilderCase(ISchemaBuilder schemaBuilder)
        {
            SchemaBuilder = schemaBuilder ?? throw new ArgumentNullException(nameof(schemaBuilder), "Schema builder is null.");
        }

        /// <summary>
        /// Builds an array schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// An <see cref="ArraySchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an <see cref="ArrayResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is ArrayResolution array))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new ArraySchema(SchemaBuilder.BuildSchema(array.ItemType, cache));
            cache.Add(array.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="BooleanResolution" />.
    /// </summary>
    public class BooleanSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a boolean schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="BooleanSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="BooleanResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is BooleanResolution boolean))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new BooleanSchema();
            cache.Add(boolean.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="ByteArrayResolution" />.
    /// </summary>
    public class BytesSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a byte array schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="BytesSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="ByteArrayResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is ByteArrayResolution bytes))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new BytesSchema();
            cache.Add(bytes.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="DecimalResolution" />.
    /// </summary>
    public class DecimalSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a decimal schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="BytesSchema" /> with a <see cref="DecimalLogicalType" /> that matches the
        /// type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="DecimalResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is DecimalResolution @decimal))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new BytesSchema()
            {
                LogicalType = new DecimalLogicalType(@decimal.Precision, @decimal.Scale)
            };

            cache.Add(@decimal.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="FloatingPointResolution" /> (double-precision).
    /// </summary>
    public class DoubleSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a double schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="DoubleSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a 16-bit <see cref="FloatingPointResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is FloatingPointResolution @double) || @double.Size != 16)
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new DoubleSchema();
            cache.Add(@double.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="DurationResolution" />.
    /// </summary>
    public class DurationSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a duration schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="StringSchema" />.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="DurationResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is DurationResolution duration))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new StringSchema();
            cache.Add(duration.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="EnumResolution" />.
    /// </summary>
    public class EnumSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds an enum schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="EnumSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an <see cref="EnumResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is EnumResolution @enum))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            if (@enum.IsFlagEnum)
            {
                var schema = new LongSchema();
                cache.Add(@enum.Type, schema);

                return schema;
            }
            else
            {
                var name = @enum.Namespace == null
                    ? @enum.Name.Value
                    : $"{@enum.Namespace.Value}.{@enum.Name.Value}";

                var schema = new EnumSchema(name);
                cache.Add(@enum.Type, schema);

                foreach (var symbol in @enum.Symbols)
                {
                    schema.Symbols.Add(symbol.Name.Value);
                }

                return schema;
            }
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="FloatingPointResolution" /> (single-precision).
    /// </summary>
    public class FloatSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a float schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="FloatSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an 8-bit <see cref="FloatingPointResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is FloatingPointResolution @float) || @float.Size != 8)
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new FloatSchema();
            cache.Add(@float.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="IntegerResolution" /> (32-bit and smaller).
    /// </summary>
    public class IntSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds an int schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// An <see cref="IntSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an <see cref="IntegerResolution" /> or specifies a
        /// size greater than 32 bits.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is IntegerResolution @int) || @int.Size > 32)
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new IntSchema();
            cache.Add(@int.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="IntegerResolution" /> (larger than 32-bit).
    /// </summary>
    public class LongSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a long schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="LongSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an <see cref="IntegerResolution" /> or specifies a
        /// size less than or equal to 32 bits.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is IntegerResolution @long) || @long.Size <= 32)
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new LongSchema();
            cache.Add(@long.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="MapResolution" />.
    /// </summary>
    public class MapSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// A schema builder instance that will be used to resolve map value types.
        /// </summary>
        public ISchemaBuilder SchemaBuilder { get; }

        /// <summary>
        /// Creates a new map schema builder case.
        /// </summary>
        /// <param name="schemaBuilder">
        /// A schema builder instance that will be used to resolve map value types.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the schema builder is null.
        /// </exception>
        public MapSchemaBuilderCase(ISchemaBuilder schemaBuilder)
        {
            SchemaBuilder = schemaBuilder ?? throw new ArgumentNullException(nameof(schemaBuilder), "Schema builder cannot be null.");
        }

        /// <summary>
        /// Builds a map schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="MapSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an <see cref="MapResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is MapResolution map))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new MapSchema(SchemaBuilder.BuildSchema(map.ValueType, cache));
            cache.Add(map.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="RecordResolution" />.
    /// </summary>
    public class RecordSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// A schema builder instance that will be used to resolve record field types.
        /// </summary>
        public ISchemaBuilder SchemaBuilder { get; }

        /// <summary>
        /// Creates a new record schema builder case.
        /// </summary>
        /// <param name="schemaBuilder">
        /// A schema builder instance that will be used to resolve record field types.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the schema builder is null.
        /// </exception>
        public RecordSchemaBuilderCase(ISchemaBuilder schemaBuilder)
        {
            SchemaBuilder = schemaBuilder ?? throw new ArgumentNullException(nameof(schemaBuilder), "Schema builder cannot be null.");
        }

        /// <summary>
        /// Builds a record schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="RecordSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not an <see cref="RecordResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is RecordResolution record))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var name = record.Namespace == null
                ? record.Name.Value
                : $"{record.Namespace.Value}.{record.Name.Value}";

            var schema = new RecordSchema(name);
            cache.Add(record.Type, schema);

            foreach (var field in record.Fields)
            {
                schema.Fields.Add(new RecordField(field.Name.Value, SchemaBuilder.BuildSchema(field.Type, cache)));
            }

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="StringResolution" />.
    /// </summary>
    public class StringSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a string schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="StringSchema" /> that matches the type resolution.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="StringResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is StringResolution @string))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new StringSchema();
            cache.Add(@string.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="TimestampResolution" />.
    /// </summary>
    public class TimestampSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a timestamp schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="StringSchema" />.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="TimestampResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is TimestampResolution timestamp))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new StringSchema();
            cache.Add(timestamp.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="UriResolution" />.
    /// </summary>
    public class UriSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a URI schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="StringSchema" />.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="UriResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is UriResolution uri))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new StringSchema();
            cache.Add(uri.Type, schema);

            return schema;
        }
    }

    /// <summary>
    /// A schema builder case that matches <see cref="UuidResolution" />.
    /// </summary>
    public class UuidSchemaBuilderCase : SchemaBuilderCase
    {
        /// <summary>
        /// Builds a UUID schema.
        /// </summary>
        /// <param name="resolution">
        /// A type resolution.
        /// </param>
        /// <param name="cache">
        /// A schema cache.
        /// </param>
        /// <returns>
        /// A <see cref="StringSchema" />.
        /// </returns>
        /// <exception cref="UnsupportedTypeException">
        /// Thrown when the resolution is not a <see cref="UuidResolution" />.
        /// </exception>
        public override Schema BuildSchema(TypeResolution resolution, IDictionary<Type, Schema> cache)
        {
            if (!(resolution is UuidResolution uuid))
            {
                throw new UnsupportedTypeException(resolution.Type);
            }

            var schema = new StringSchema()
            {
                LogicalType = new UuidLogicalType()
            };

            cache.Add(uuid.Type, schema);

            return schema;
        }
    }
}
