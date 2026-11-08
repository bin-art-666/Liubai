namespace Liubai;

// Deterministic time-based policy; each monitor owns an independent instance.
internal sealed class BarVisibilityPolicy
{
    internal bool Visible {get;private set;}=true;
    long edgeSince=-1,leaveSince=-1;
    internal void Reset(bool visible){Visible=visible;edgeSince=leaveSince=-1;}
    internal void Reveal(){Visible=true;edgeSince=leaveSince=-1;}
    internal bool Update(bool hiddenMode,bool edge,bool inside,bool protectedOperation,long now)
    {
        if(!hiddenMode){Reset(true);return true;}
        if(protectedOperation){Reveal();return true;}
        if(Visible)
        {
            edgeSince=-1;
            if(inside){leaveSince=-1;return true;}
            if(leaveSince<0)leaveSince=now;
            if(now-leaveSince>=700){Visible=false;leaveSince=-1;}
        }
        else
        {
            leaveSince=-1;
            if(!edge)edgeSince=-1;
            else {if(edgeSince<0)edgeSince=now;if(now-edgeSince>=200)Reveal();}
        }
        return Visible;
    }
    internal static bool HotEdge(int x,int y,int left,int top,int width,int height,int thickness)
    {
        long local=(long)x-left;
        return width>0&&height>0&&local>=0&&local<width&&y>=top&&(long)y-top<Math.Max(1,thickness)
            &&(local*3<width||local*3>=2L*width);
    }
}
