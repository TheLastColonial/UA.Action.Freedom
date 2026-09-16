Feature: Locations API
    The deployed Freedom service exposes distribution hubs at /locations and the bays within
    them at /locations/{id}/bays. Reads are open to every operational role; writes —
    creating a location, or adding, renaming or removing a bay — are Administrator only.

    A bay is a 1m by 1m storage area within a location, identified by a code that only has
    to be unique within its own location: two depots may each have a bay called "A1".

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/locations"

Scenario: Reading locations requires a token
    When I GET "/locations" without a token
    Then the response status is 401

Scenario: A loader may read locations but not create one
    Given I am authenticated as "operator"
    When I GET "/locations"
    Then the response status is 200
    When I POST "/locations" with body:
        """
        { "name": "Coventry Depot" }
        """
    Then the response status is 403

Scenario: An administrator creates a location
    Given I am authenticated as "admin"
    When I POST "/locations" with body:
        """
        { "name": "Coventry Depot", "city": "Coventry", "postcode": "CV1 2AB" }
        """
    Then the response status is 201
    And the "Location" header names a new resource
    When I GET "/locations/{id}"
    Then the response status is 200
    And the response body field "name" is "Coventry Depot"

Scenario: Fetching an unknown location is a 404
    Given I am authenticated as "operator"
    When I GET "/locations/99999999"
    Then the response status is 404

Scenario: An administrator adds a bay to a location
    Given I am authenticated as "admin"
    And a location exists
    When I POST "/locations/{id}/bays" on the remembered location with body:
        """
        { "code": "A1" }
        """
    Then the response status is 201
    When I GET "/locations/{id}/bays" on the remembered location
    Then the response status is 200
    And the response body is a list of 1 or more

Scenario: Two bays at the same location cannot share a code
    Given I am authenticated as "admin"
    And a location exists
    And a bay exists at the location
    When I POST "/locations/{id}/bays" on the remembered location with body:
        """
        { "code": "A1" }
        """
    Then the response status is 409

Scenario: Adding a bay to an unknown location is a 404
    Given I am authenticated as "admin"
    When I POST "/locations/99999999/bays" with body:
        """
        { "code": "A1" }
        """
    Then the response status is 404
