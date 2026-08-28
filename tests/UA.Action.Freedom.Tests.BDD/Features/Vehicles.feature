Feature: Vehicles API
    The deployed Freedom service exposes CRUD for donated vehicles at /vehicles.
    VIN is the natural key, reads are open to every operational role, writes are
    limited to Purchaser and Administrator, and the Ground Officer is excluded.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API is reachable
    And no vehicle exists with VIN "WDB9066331S0BDD01"

Scenario: Listing vehicles requires a token
    When I GET "/vehicles" without a token
    Then the response status is 401

Scenario: A ground officer is refused vehicle reads
    Given I am authenticated as "groundofficer"
    When I GET "/vehicles"
    Then the response status is 403

Scenario: A ground officer is refused vehicle writes
    Given I am authenticated as "groundofficer"
    When I POST "/vehicles" with body:
        """
        { "vin": "WDB9066331S0BDD01", "plate": "UA10ACT", "year": 2014, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2200 }
        """
    Then the response status is 403

Scenario: A purchaser records a new vehicle
    Given I am authenticated as "operator"
    When I POST "/vehicles" with body:
        """
        { "vin": "WDB9066331S0BDD01", "plate": "UA10ACT", "brand": "Mercedes-Benz", "model": "Sprinter", "year": 2014, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2200 }
        """
    Then the response status is 201
    And the "Location" header ends with "/vehicles/WDB9066331S0BDD01"

Scenario: A recorded vehicle can be fetched by VIN
    Given I am authenticated as "operator"
    And a vehicle exists with VIN "WDB9066331S0BDD01"
    When I GET "/vehicles/WDB9066331S0BDD01"
    Then the response status is 200
    And the response body field "plate" is "UA10ACT"
    And the response body field "fuel" is "Diesel"

Scenario: A recorded vehicle appears in the list
    Given I am authenticated as "operator"
    And a vehicle exists with VIN "WDB9066331S0BDD01"
    When I GET "/vehicles?page=1&pageSize=200"
    Then the response status is 200
    And the response body lists a vehicle with VIN "WDB9066331S0BDD01"

Scenario: Recording a vehicle whose VIN is taken is a conflict
    Given I am authenticated as "operator"
    And a vehicle exists with VIN "WDB9066331S0BDD01"
    When I POST "/vehicles" with body:
        """
        { "vin": "WDB9066331S0BDD01", "plate": "UA10ACT", "year": 2014, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2200 }
        """
    Then the response status is 409

Scenario: A vehicle with a blank VIN is rejected
    Given I am authenticated as "operator"
    When I POST "/vehicles" with body:
        """
        { "vin": "", "plate": "X", "year": 1000, "fuel": "Diesel", "transmission": "Manual", "weightKg": -5 }
        """
    Then the response status is 400
    And the response body names "Vin" as invalid

Scenario: An administrator updates a vehicle
    Given I am authenticated as "operator"
    And a vehicle exists with VIN "WDB9066331S0BDD01"
    And I am authenticated as "admin"
    When I PUT "/vehicles/WDB9066331S0BDD01" with body:
        """
        { "plate": "UA99UPD", "year": 2014, "fuel": "Hybrid", "transmission": "Automatic", "weightKg": 2350, "servicing": true }
        """
    Then the response status is 204
    When I GET "/vehicles/WDB9066331S0BDD01"
    Then the response body field "plate" is "UA99UPD"
    And the response body field "weightKg" is "2350"

Scenario: Updating an unknown vehicle is a 404
    Given I am authenticated as "operator"
    When I PUT "/vehicles/NOSUCHVIN0000BDD1" with body:
        """
        { "plate": "UA00XXX", "year": 2014, "fuel": "Diesel", "transmission": "Manual", "weightKg": 2000 }
        """
    Then the response status is 404

Scenario: A purchaser deletes a vehicle
    Given I am authenticated as "operator"
    And a vehicle exists with VIN "WDB9066331S0BDD01"
    When I DELETE "/vehicles/WDB9066331S0BDD01"
    Then the response status is 204
    When I GET "/vehicles/WDB9066331S0BDD01"
    Then the response status is 404

Scenario: Deleting an unknown vehicle is a 404
    Given I am authenticated as "operator"
    When I DELETE "/vehicles/NOSUCHVIN0000BDD1"
    Then the response status is 404
