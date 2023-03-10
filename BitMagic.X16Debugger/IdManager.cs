namespace BitMagic.X16Debugger;

public enum ObjectType
{
    Unknown = 0,
    Variable,
    Stack,
    DecompiledData
}

public class IdManager
{
    private int _id = 1;
    public int GetId() => _id++;

    private readonly Dictionary<int, ObjectContainer> _objects = new Dictionary<int, ObjectContainer>();

    public int AddObject(object obj, ObjectType objectType)
    {
        var id = GetId();
        _objects.Add(id, new ObjectContainer { Id = id, ObjectType = objectType, Object = obj });
        return id;
    }

    public void UpdateObject(int id, object obj)
    {
        _objects[id].Object = obj;
    }

    public T? GetObject<T>(int id) where T : class
    {
        if (_objects.ContainsKey(id))
            return _objects[id].Object as T;

        return null;
    }

    public void Clear()
    {
        _objects.Clear();
        _id = 1;
    }

    public IEnumerable<T> GetObjects<T>(ObjectType objectType) where T : class
    {
        foreach (var i in _objects.Values.Where(i => i.ObjectType == objectType))
        {
            var obj = i.Object as T;
            if (obj != null) 
                yield return obj;
        }
    }
}

internal class ObjectContainer
{
    public int Id { get; init; }
    public ObjectType ObjectType { get; init; }
    public object? Object { get; set; }
}