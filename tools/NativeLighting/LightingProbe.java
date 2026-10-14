import java.io.*;
import java.lang.reflect.*;
import java.nio.*;
import java.nio.file.*;
import java.util.*;

/** Original isolated adapter for the installed game's LightingJNI ABI. No game code or libraries are bundled. */
public final class LightingProbe {
    static final int FLOOR=1, WALL_N=2, WALL_W=4, OPAQUE=8, WINDOW_N=16, WINDOW_W=32,
        DOOR_N=64, DOOR_W=128, OPEN_N=256, OPEN_W=512, ELEVATED=1024, OPEN_AIR=2048, THUMP_N=4096, THUMP_W=8192;
    static final int[] DX={0,-1,-1,-1,0,1,1,1}, DY={-1,-1,0,1,1,1,0,-1};
    static final Map<String,Method> methods=new HashMap<>();
    static Class<?> engine;
    static int width,height,minZ,maxZ,startX,startY;
    static int[] flags;
    static long[] buildings,rooms;
    static int index(int x,int y,int z){return (z-minZ)*width*height+y*width+x;}
    static int at(int x,int y,int z){return x<0||y<0||x>=width||y>=height||z<minZ||z>maxZ?0:Math.max(0,flags[index(x,y,z)]);}
    static boolean northBlocked(int f){return (f&WALL_N)!=0||((f&DOOR_N)!=0&&(f&OPEN_N)==0);}
    static boolean westBlocked(int f){return (f&WALL_W)!=0||((f&DOOR_W)!=0&&(f&OPEN_W)==0);}
    static boolean cardinal(int x,int y,int z,int dx,int dy){
        int a=at(x,y,z),b=at(x+dx,y+dy,z);
        if((a&OPAQUE)!=0||(b&OPAQUE)!=0)return false;
        return dx<0?!westBlocked(a):dx>0?!westBlocked(b):dy<0?!northBlocked(a):!northBlocked(b);
    }
    static int visibility(int x,int y,int z){
        int result=0;
        for(int i=0;i<8;i++){
            int dx=DX[i],dy=DY[i];
            boolean clear=dx==0||dy==0?cardinal(x,y,z,dx,dy):
                cardinal(x,y,z,dx,0)&&cardinal(x,y,z,0,dy)&&cardinal(x+dx,y,z,0,dy)&&cardinal(x,y+dy,z,dx,0);
            if(clear)result|=1<<i;
        }
        return result;
    }
    static int visionMatrix(int x,int y,int z){
        int matrix=0,visible=visibility(x,y,z);
        for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
            if(dx==0&&dy==0&&dz==0)continue;
            boolean blocked=dz>0?(at(x+dx,y+dy,z+1)&FLOOR)!=0:dz<0?(at(x,y,z)&FLOOR)!=0:false;
            if(dx!=0||dy!=0){int direction=0;while(DX[direction]!=dx||DY[direction]!=dy)direction++;blocked|=(visible&(1<<direction))==0;}
            if(blocked)matrix|=1<<((dx+1)+(dy+1)*3+(dz+1)*9);
        }
        return matrix;
    }
    static Object call(String name,Object...args)throws Exception{
        Method method=methods.get(name);
        if(method==null){
            Class<?>[] types=new Class<?>[args.length];
            for(int i=0;i<args.length;i++)types[i]=args[i] instanceof Integer?int.class:args[i] instanceof Long?long.class:
                args[i] instanceof Float?float.class:args[i] instanceof Boolean?boolean.class:args[i].getClass();
            method=engine.getMethod(name,types);methods.put(name,method);
        }
        return method.invoke(null,args);
    }
    static int bounded(int value,int low,int high,String label){if(value<low||value>high)throw new IllegalArgumentException(label);return value;}
    static float unit(float v){if(!Float.isFinite(v)||v<0||v>1)throw new IllegalArgumentException("Color outside 0..1");return v;}
    public static void main(String[] args)throws Exception{
        if(args.length!=3)throw new IllegalArgumentException("Expected library, input and output paths");
        byte[] bytes=Files.readAllBytes(Path.of(args[1]));if(bytes.length>24_000_000)throw new IllegalArgumentException("Lighting input exceeds 24 MB");
        ByteBuffer in=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        if(in.getInt()!=0x324c5a50)throw new IllegalArgumentException("Unknown lighting protocol");
        int chunksX=bounded(in.getInt(),1,16,"Chunk width"),chunksY=bounded(in.getInt(),1,16,"Chunk height");
        int worldChunkX=bounded(in.getInt(),0,40000,"World chunk X"),worldChunkY=bounded(in.getInt(),0,40000,"World chunk Y");
        startX=worldChunkX*8;startY=worldChunkY*8;width=chunksX*8;height=chunksY*8;
        minZ=bounded(in.getInt(),-16,31,"Minimum floor");maxZ=bounded(in.getInt(),minZ,31,"Maximum floor");
        int level=bounded(in.getInt(),minZ,maxZ,"Output floor");
        int outX=bounded(in.getInt(),0,width-1,"Output X"),outY=bounded(in.getInt(),0,height-1,"Output Y");
        int size=bounded(in.getInt(),8,64,"Output size");if(outX+size>width||outY+size>height)throw new IllegalArgumentException("Output exceeds grid");
        float skyR=unit(in.getFloat()),skyG=unit(in.getFloat()),skyB=unit(in.getFloat()),ambient=unit(in.getFloat());
        // The lighting library consumes a packed ABGR color, as also returned in its vertex samples.
        int sky=(int)(skyR*255)|(int)(skyG*255)<<8|(int)(skyB*255)<<16;
        int total=width*height*(maxZ-minZ+1);flags=new int[total];Arrays.fill(flags,-1);buildings=new long[total];rooms=new long[total];
        int count=bounded(in.getInt(),0,500000,"Square count");
        for(int i=0;i<count;i++){
            int x=bounded(in.getInt(),0,width-1,"Square X"),y=bounded(in.getInt(),0,height-1,"Square Y"),z=bounded(in.getInt(),minZ,maxZ,"Square floor"),f=bounded(in.getInt(),0,16383,"Square flags"),p=index(x,y,z);
            if(flags[p]!=-1)throw new IllegalArgumentException("Duplicate square");flags[p]=f;buildings[p]=in.getLong();rooms[p]=in.getLong();
        }
        int points=bounded(in.getInt(),0,4096,"Light count");float[][] lights=new float[points][8];
        for(float[] l:lights){l[0]=in.getInt();l[1]=in.getInt();l[2]=bounded(in.getInt(),minZ,maxZ,"Light floor");l[3]=bounded(in.getInt(),1,20,"Light radius");for(int i=4;i<7;i++)l[i]=unit(in.getFloat());l[7]=in.get()!=0?1:0;}
        int roomCount=bounded(in.getInt(),0,4096,"Room light count");long[][] roomLights=new long[roomCount][8];
        for(long[] l:roomLights){l[0]=in.getLong();l[1]=in.getLong();for(int i=2;i<7;i++)l[i]=in.getInt();l[7]=in.get()!=0?1:0;}
        if(in.hasRemaining())throw new IllegalArgumentException("Trailing lighting input");
        System.load(Path.of(args[0]).toRealPath().toString());engine=Class.forName("zombie.iso.LightingJNI");call("configure",0.005f);
        try{
            for(int frame=0;frame<12;frame++){
                call("stateBeginUpdate",0,worldChunkX,worldChunkY,chunksX,chunksY);
                call("playerSet",startX+outX+size/2f,startY+outY+size/2f,level+32f,0f,1f,false,false,false,false,0f,100f,-1f);
                call("stateEndFrame",1f,1f,1f,ambient,1f-ambient,100f,100f,true,100f,sky);
                if(frame==0){
                    for(int cy=0;cy<chunksY;cy++)for(int cx=0;cx<chunksX;cx++){
                        call("chunkBeginUpdate",worldChunkX+cx,worldChunkY+cy,minZ+32,maxZ+32);
                        for(int z=minZ;z<=maxZ;z++){
                            call("chunkLevelBeginUpdate",z+32);
                            for(int yy=0;yy<8;yy++)for(int xx=0;xx<8;xx++){
                                int x=cx*8+xx,y=cy*8+yy,p=index(x,y,z),f=flags[p];
                                if(f==-1){call("squareSetNull",xx,yy,z+32);continue;}
                                call("squareBeginUpdate",xx,yy,z+32);
                                call("squareSet",visibility(x,y,z),(at(x,y,z+1)&FLOOR)==0,(f&FLOOR)==0,(f&ELEVATED)!=0,visionMatrix(x,y,z),buildings[p],rooms[p],(f&OPEN_AIR)!=0?sky:0,(f&OPEN_AIR)!=0);
                                if((f&DOOR_N)!=0){if((f&THUMP_N)!=0)call("squareAddThumpable",true,(f&OPEN_N)!=0,true,(f&OPEN_N)!=0);else call("squareAddDoor",true,(f&OPEN_N)!=0,false);}
                                if((f&DOOR_W)!=0){if((f&THUMP_W)!=0)call("squareAddThumpable",false,(f&OPEN_W)!=0,true,(f&OPEN_W)!=0);else call("squareAddDoor",false,(f&OPEN_W)!=0,false);}
                                if((f&WINDOW_N)!=0)call("squareAddWindow",true,false,false);
                                if((f&WINDOW_W)!=0)call("squareAddWindow",false,false,false);
                                call("squareSetLightTransmission",0f,0f,0f,(f&WALL_W)!=0?0f:Float.MAX_VALUE,0f,0f,0f,0f,(f&WALL_N)!=0?0f:Float.MAX_VALUE,0f,0f,0f,0f,(at(x+1,y,z)&WALL_W)!=0?0f:Float.MAX_VALUE,0f,0f,0f,0f,(at(x,y+1,z)&WALL_N)!=0?0f:Float.MAX_VALUE,0f);
                                call("squareEndUpdate");
                            }
                            call("chunkLevelEndUpdate");
                        }
                        call("chunkEndUpdate");
                    }
                    for(int i=0;i<lights.length;i++){float[] l=lights[i];call("addLight",i+1,(int)l[0],(int)l[1],(int)l[2]+32,(int)l[3],Math.min(1f,l[4]*2f),Math.min(1f,l[5]*2f),Math.min(1f,l[6]*2f),-1,l[7]!=0);}
                    for(int i=0;i<roomLights.length;i++){long[] l=roomLights[i];call("addRoomLight",100000+i,l[0],l[1],(int)l[2],(int)l[3],(int)l[4]+32,(int)l[5],(int)l[6],l[7]!=0);}
                }
                call("stateEndUpdate");call("DoLightingUpdateNew",System.nanoTime(),true);
            }
            try(PrintWriter out=new PrintWriter(Files.newBufferedWriter(Path.of(args[2])))){
                out.print("{\"protocol\":2,\"counter\":"+call("stateUpdateCounter",0)+",\"cells\":[");boolean first=true;
                for(int y=outY;y<outY+size;y++)for(int x=outX;x<outX+size;x++){
                    if(flags[index(x,y,level)]<0)continue;
                    int[] values=new int[43];if(!Boolean.TRUE.equals(call("getSquareLighting",0,startX+x,startY+y,level+32,values)))continue;
                    if(!first)out.print(',');first=false;out.print('[');out.print((x-outX)+","+(y-outY)+","+level);
                    for(int channel=0;channel<3;channel++)out.print(","+call("getLightInfo",0,startX+x,startY+y,level+32,channel));
                    out.print(","+call("getDarkMulti",0,startX+x,startY+y,level+32));for(int i=0;i<8;i++)out.print(","+values[5+i]);out.print(']');
                }
                out.print("]}");
            }
        }finally{call("destroy");}
    }
}
