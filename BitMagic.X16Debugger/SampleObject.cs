using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debug;

internal abstract class SampleObject<TProtocolObject, TFormat>
    where TProtocolObject : class
    where TFormat : class
{
    protected SampleObject(X16Debug adapter)
    {
        this.Adapter = adapter;
    }

    protected X16Debug Adapter { get; private set; }

    protected abstract bool IsSameFormat(TFormat a, TFormat b);

    protected abstract TProtocolObject CreateProtocolObject();

    protected TProtocolObject? ProtocolObject { get; private set; }

    protected TFormat? Format { get; private set; }

    public virtual void Invalidate()
    {
        this.Format = null;
        this.ProtocolObject = null;
    }

    public TProtocolObject GetProtocolObject(TFormat format)
    {
        if (this.ProtocolObject == null || !this.IsSameFormat(format, this.Format ?? throw new Exception("Format is null")))
        {
            this.Format = format;
            this.ProtocolObject = this.CreateProtocolObject();
        }

        return this.ProtocolObject;
    }
}