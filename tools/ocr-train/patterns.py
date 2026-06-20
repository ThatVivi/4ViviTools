import random

def _num(lo, hi): return str(random.randint(lo, hi))
def _pair(lo, hi):
    mx = random.randint(lo, hi); cur = random.randint(0, mx); return f"{cur}/{mx}"

ROLE_PATTERNS = {
    "HP": lambda: _pair(50, 999999),
    "SP": lambda: _pair(10, 99999),
    "BaseLevel": lambda: _num(1, 999),
    "JobLevel": lambda: _num(1, 70),
    "Zeny": lambda: _num(0, 2000000000),
    "Weight": lambda: _pair(0, 30000),
    "PosX": lambda: _num(0, 400),
    "PosY": lambda: _num(0, 400),
    "CharName": lambda: random.choice(["Vivi", "DarkLord", "Aizen", "ProtoVivi", "GMVivi", "Eldrynn"]),
    "ClassName": lambda: random.choice(["Rune Knight", "Warlock", "Ranger", "Rebellion", "Star Emperor"]),
}

def sample_value(role: str) -> str:
    return ROLE_PATTERNS[role]()
