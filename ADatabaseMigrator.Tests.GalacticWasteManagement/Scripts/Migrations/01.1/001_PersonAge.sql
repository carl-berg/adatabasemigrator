ALTER TABLE Person
    ADD Age INT NOT NULL 
    CONSTRAINT DF_Age DEFAULT(0)