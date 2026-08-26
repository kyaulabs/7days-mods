#!/usr/bin/env python3
"""Minimal .ttp (PlayerDataFile) parser - extracts inventory item quality/durability."""
import struct, sys

class BR:
    def __init__(self, data):
        self.d = data
        self.p = 0
    def byte(self): v = self.d[self.p]; self.p += 1; return v
    def bool(self): return self.byte() != 0
    def i16(self): v = struct.unpack_from('<h', self.d, self.p)[0]; self.p += 2; return v
    def u16(self): v = struct.unpack_from('<H', self.d, self.p)[0]; self.p += 2; return v
    def i32(self): v = struct.unpack_from('<i', self.d, self.p)[0]; self.p += 4; return v
    def u32(self): v = struct.unpack_from('<I', self.d, self.p)[0]; self.p += 4; return v
    def f32(self): v = struct.unpack_from('<f', self.d, self.p)[0]; self.p += 4; return v
    def i64(self): v = struct.unpack_from('<q', self.d, self.p)[0]; self.p += 8; return v
    def skip(self, n): self.p += n
    def leb(self):
        ln = 0; shift = 0
        while True:
            b = self.byte()
            ln |= (b & 0x7f) << shift
            if not (b & 0x80): break
            shift += 7
        return ln
    def string(self):
        ln = 0; shift = 0
        while True:
            b = self.byte()
            ln |= (b & 0x7f) << shift
            if not (b & 0x80): break
            shift += 7
        s = self.d[self.p:self.p+ln].decode('utf-8', 'replace')
        self.p += ln
        return s

def read_itemvalue(br, indent=''):
    ver = br.byte()
    if ver == 0:
        return {'empty': True}
    flags = 0
    if ver >= 8: flags = br.byte()
    typ = br.u16()
    if flags & 1: typ += 32768
    usetimes = br.f32() if ver > 5 else br.u16()
    quality = br.u16()
    meta = br.u16()
    if meta >= 65535: meta = -1
    meta_items = []
    if ver > 6:
        n = br.byte()
        for _ in range(n):
            key = br.string()
            tt = br.i32()
            if tt == 0: val = br.f32()
            elif tt == 1: val = br.i32()
            else: val = br.string()
            meta_items.append((key, val))
    stats = []
    if flags & 2:
        n = br.byte()
        for _ in range(n):
            pe = br.byte(); b = br.i16(); a = br.i16()
            stats.append((pe, b, a))
    mods = []
    cosmetics = []
    if (ver > 4 or quality > 0):
        n = br.byte()
        for _ in range(n):
            if br.bool(): mods.append(read_itemvalue(br))
        n = br.byte()
        for _ in range(n):
            if br.bool(): cosmetics.append(read_itemvalue(br))
    activated = br.byte() if ver > 1 else 0
    ammo = br.byte() if ver > 2 else 0
    seed = br.u16() if ver > 3 else 0
    if ver > 8 and br.bool():
        br.i64()
    return {'type': typ, 'use': usetimes, 'quality': quality, 'meta': meta, 'flags': flags, 'ver': ver,
            'meta_items': meta_items, 'stats': stats, 'mods': mods}

def read_bodydamage(br, b):
    if b > 21:
        ver = br.i32()
        if ver >= 4: br.i32()
        if ver >= 3: br.u32()
    elif b > 20:
        pass
    elif b > 19:
        br.i32()

def read_stat(br):
    v = br.i32()
    br.f32()          # m_value
    br.f32()          # m_maxModifier
    if v <= 5:
        br.f32()
    br.f32()          # m_baseMax
    br.f32()          # m_originalBaseMax
    br.f32()          # m_originalValue

def read_entitystats(br):
    num = br.i32()
    read_stat(br)  # Health
    read_stat(br)  # Stamina
    if num <= 10:
        read_stat(br)
    read_stat(br)  # Water
    read_stat(br)  # Food
    if num >= 11:
        br.byte()  # CoreTemp sbyte

def read_bag(br):
    b = br.byte()
    n = br.u16()
    for _ in range(n):
        cnt = br.u16()
        if cnt: read_itemvalue(br)
    if br.bool():
        ln = br.leb()
        br.skip((ln + 7) // 8)
    if b >= 1:
        br.bool()  # Touched
        if br.bool():
            # PreferenceTracker.Read
            br.i32()
            if br.bool(): read_itemstack_arr(br)
            if br.bool():
                n2 = br.u16()
                for _ in range(n2):
                    if br.bool(): read_itemvalue(br)
            if br.bool(): read_itemstack_arr(br)

def read_itemstack_arr(br):
    n = br.u16()
    for _ in range(n):
        cnt = br.u16()
        if cnt: read_itemvalue(br)

def read_playerprofile(br):
    v = br.i32()
    br.string(); br.bool(); br.string(); br.byte()
    if v > 1: br.string()
    if v > 2: br.string()
    if v > 3: br.string(); br.string(); br.string()
    if v > 4: br.string()

def read_traderdata(br):
    br.i32()          # TraderID
    br.skip(8)        # lastInventoryUpdate ulong
    ver = br.byte()
    if ver < 2:
        n = br.u16()
        for _ in range(n):
            cnt = br.u16()
            if cnt: read_itemvalue(br)
        ntier = br.byte()
        for _ in range(ntier):
            n2 = br.u16()
            for _ in range(n2):
                cnt = br.u16()
                if cnt: read_itemvalue(br)
        br.i32()      # AvailableMoney
        n3 = br.i32()
        br.skip(n3)   # markups
    else:
        n = br.i32()
        for _ in range(n):
            cnt = br.u16()
            if cnt: read_itemvalue(br)
            br.byte()       # markup sbyte
            br.bool()       # AddedByPlayer
        ntier = br.byte()
        for _ in range(ntier):
            n2 = br.u16()
            for _ in range(n2):
                cnt = br.u16()
                if cnt: read_itemvalue(br)
        br.i32()      # AvailableMoney

def read_ecd(br):
    b = br.byte()
    entity_class = br.i32()
    is_player = entity_class in (0x774BC5CE, 0x12345678)  # hash(playerMale); female hash TBD
    br.i32()          # id
    br.f32()          # lifetime
    br.f32(); br.f32(); br.f32()  # pos
    br.f32(); br.f32(); br.f32()  # rot
    br.bool()         # onGround
    read_bodydamage(br, b)
    if b >= 8:
        if br.bool():
            read_entitystats(br)
    else:
        br.i16(); br.i16()
        if b >= 7: br.i16(); br.i16()
    br.i16()          # deathTime
    if b >= 35:
        if br.bool(): read_bag(br)
    elif b >= 2 and br.bool():
        br.i32()
    if b >= 3:
        br.i32(); br.i32(); br.i32()  # homePosition
        br.i16()                      # homeRange
    if b >= 5:
        br.byte()     # spawnerSource
    if is_player:
        read_itemvalue(br)  # holdingItem
        br.byte()           # teamNumber
        br.string()         # entityName
        br.string()         # skinTexture
        if b > 12 and br.bool():
            read_playerprofile(br)
    if b > 9:
        n = br.u16()
        br.skip(n)
    if b > 23 and br.bool():
        read_traderdata(br)
    if b >= 36:
        br.f32()
    return is_player

def parse(path):
    data = open(path, 'rb').read()
    br = BR(data)
    magic = bytes(br.d[0:4])
    assert magic == b'ttp\0', magic
    br.skip(4)
    version = br.byte()
    print(f'== {path} (ttp version {version})')
    if version <= 37:
        print('  old format, skipping')
        return
    is_player = read_ecd(br)
    n = br.u16()
    print(f'  inventory slots: {n}')
    names = {}
    idx = 0
    for i in range(n):
        cnt = br.u16()
        if cnt > 0:
            iv = read_itemvalue(br)
            names[idx] = (cnt, iv)
        idx += 1
    # map type ids to item names via ConfigsDump items.xml ids? use vanilla nameIdMapping from save
    for slot, (cnt, iv) in names.items():
        if iv.get('empty'): continue
        nm = NAMES.get(iv['type']) or NAMES.get(iv['type'] - 32768) or f'id{iv["type"]}'
        print(f'  slot {slot}: count={cnt} {nm} type={iv["type"]} ver={iv["ver"]} flags={iv["flags"]} quality={iv["quality"]} useTimes={iv["use"]:.1f} mods={len(iv["mods"])} meta={iv["meta_items"]}')

NAMES = {1:'StoneAxe',2:'TazasStoneAxe',3:'ClawHammer',4:'Nailgun',5:'IronFireaxe',6:'SteelAxe',7:'IronPickaxe',8:'SteelPickaxe',9:'StoneShovel',10:'IronShovel',11:'SteelShovel',12:'Chainsaw',13:'Auger',14:'Wrench',15:'Ratchet',16:'ImpactDriver'}

for p in sys.argv[1:]:
    parse(p)
